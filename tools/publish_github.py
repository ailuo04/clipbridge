"""Publish validated ClipBridge binaries to GitHub without exposing secrets."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

REPO = 'https://github.com/ailuo04/clipbridge'
API = 'https://api.github.com/repos/ailuo04/clipbridge'
RAW = 'https://raw.githubusercontent.com/ailuo04/clipbridge/master'


class PublishError(RuntimeError):
    pass


def version_tuple(value):
    if not re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)', value):
        raise PublishError('Version must be X.Y.Z (no leading zeros).')
    parts = tuple(map(int, value.split('.')))
    if any(number > 65534 for number in parts):
        raise PublishError('Version components exceed Windows assembly limits.')
    return parts


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def validate_bundle(directory, version):
    version_tuple(version)
    envelope_path = directory / 'latest-v2.json'
    manifest_path = directory / f'manifest-{version}.json'
    envelope = envelope_path.read_bytes()
    if envelope != manifest_path.read_bytes():
        raise PublishError('Version manifest does not match channel manifest.')
    try:
        envelope_json = json.loads(envelope)
        payload = json.loads(base64.b64decode(envelope_json['payload'], validate=True))
    except (KeyError, ValueError, json.JSONDecodeError) as error:
        raise PublishError('Signed manifest is not valid JSON/base64.') from error
    win = payload.get('windows')
    if payload.get('schema') != 1 or not isinstance(win, dict) or win.get('version') != version:
        raise PublishError('Manifest version/schema mismatch.')
    zip_name = f'ClipBridge-{version}-win-x64.zip'
    setup_name = f'ClipBridge-{version}-win-x64-Setup.exe'
    expected = (
        (zip_name, 'size', 'sha256', 'url', f'{RAW}/{zip_name}'),
        (setup_name, 'installerSize', 'installerSha256', 'installerUrl', f'{REPO}/releases/download/v{version}/{setup_name}'),
    )
    for name, size_key, hash_key, url_key, expected_url in expected:
        content = (directory / name).read_bytes()
        if len(content) != win.get(size_key) or sha256(content) != win.get(hash_key) or win.get(url_key) != expected_url:
            raise PublishError(f'Bundle mismatch: {name}')
        if not 0 < len(content) < 100 * 1024 * 1024:
            raise PublishError('Artifact exceeds the supported size limit.')
    if (directory / 'release-notes.txt').read_text(encoding='utf-8') != win.get('notes', ''):
        raise PublishError('Release notes mismatch.')
    return envelope, win


class GitHub:
    def __init__(self, token):
        self.token = token
        self.headers = {
            'Authorization': 'Bearer ' + token,
            'User-Agent': 'ClipBridge-CloudRelease/1',
            'Accept': 'application/vnd.github+json',
            'X-GitHub-Api-Version': '2022-11-28',
        }

    def api(self, path, method='GET', data=None, missing=False):
        url = path if path.startswith('https://') else API + path
        headers = dict(self.headers)
        body = None
        if data is not None:
            body = json.dumps(data, ensure_ascii=False).encode('utf-8')
            headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(url, data=body, method=method, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=600 if method == 'POST' else 90) as response:
                result = response.read()
                return json.loads(result) if result else None
        except urllib.error.HTTPError as error:
            if missing and error.code == 404:
                return None
            raise PublishError(f'GitHub API {method} {urllib.parse.urlsplit(url).path}: HTTP {error.code}') from None
        except (urllib.error.URLError, TimeoutError) as error:
            reason = type(error.reason).__name__ if isinstance(error, urllib.error.URLError) else type(error).__name__
            raise PublishError(f'GitHub API {method} connection failed ({reason}); resume with the same artifacts.') from None

    def content(self, name):
        path = '/contents/' + urllib.parse.quote(name, safe='/') + '?ref=master'
        return self.api(path, missing=True)

    def write_file(self, name, content, immutable=False):
        old = self.content(name)
        if old:
            previous = base64.b64decode(old['content'])
            if previous == content:
                return old.get('commit', {}).get('sha')
            if immutable:
                raise PublishError(f'Refusing to replace an existing versioned file: {name}')
        data = {
            'message': f'Publish {name}',
            'content': base64.b64encode(content).decode('ascii'),
            'branch': 'master',
        }
        if old:
            data['sha'] = old['sha']
        result = self.api('/contents/' + urllib.parse.quote(name, safe='/'), 'PUT', data)
        return result['commit']['sha']

    def release_by_tag(self, tag):
        return self.api('/releases/tags/' + urllib.parse.quote(tag, safe=''), missing=True)

    def assets(self, release):
        return self.api(f'/releases/{release["id"]}/assets?per_page=100')

    def upload(self, release, path):
        upload_url = release['upload_url'].split('{', 1)[0] + '?name=' + urllib.parse.quote(path.name)
        content = path.read_bytes()
        headers = dict(self.headers)
        headers['Content-Type'] = 'application/octet-stream'
        headers['Content-Length'] = str(len(content))
        request = urllib.request.Request(upload_url, data=content, method='POST', headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=3600) as response:
                result = response.read()
                return json.loads(result)
        except urllib.error.HTTPError as error:
            raise PublishError(f'GitHub asset upload failed: HTTP {error.code}') from None
        except (urllib.error.URLError, TimeoutError) as error:
            reason = type(error.reason).__name__ if isinstance(error, urllib.error.URLError) else type(error).__name__
            raise PublishError(f'GitHub asset upload failed ({reason}); resume with the same artifacts.') from None

    def update_release(self, release, body, draft=False):
        return self.api(f'/releases/{release["id"]}', 'PATCH', {
            'name': release['name'],
            'body': body,
            'draft': draft,
            'prerelease': False,
        })


def public_bytes(url, expected_size=None, expected_hash=None, max_bytes=2 * 1024 * 1024):
    for attempt in range(5):
        try:
            request = urllib.request.Request(url, headers={'User-Agent': 'ClipBridge-ReleaseVerify/1'})
            digest = hashlib.sha256()
            length = 0
            chunks = []
            with urllib.request.urlopen(request, timeout=300) as response:
                while chunk := response.read(1024 * 1024):
                    length += len(chunk)
                    if length > (expected_size if expected_size is not None else max_bytes):
                        raise PublishError('Public download larger than expected.')
                    digest.update(chunk)
                    if expected_size is None:
                        chunks.append(chunk)
            if expected_size is not None:
                if length != expected_size or digest.hexdigest() != expected_hash:
                    raise PublishError('Public download hash/size mismatch.')
                return None
            return b''.join(chunks)
        except (urllib.error.URLError, TimeoutError, PublishError):
            if attempt == 4:
                raise PublishError('Anonymous download verification failed: ' + url) from None
            time.sleep(5 * (attempt + 1))


def release_body(version, win, source_sha):
    return (
        f'## Windows {version}\n\n{win["notes"]}\n\n'
        '**升级说明：**0.3.8 用户需要手动安装一次本版；从 0.3.9 开始，应用改用 GitHub 更新清单和新的签名密钥，后续版本可自动更新。\n\n'
        '普通用户不需要任何发布密钥。安装器未进行 Authenticode 代码签名；干净电脑首次安装、原版原位升级和真实生产网络更新仍需按实际环境验证。\n\n'
        f'ZIP SHA-256: {win["sha256"]}\n\nSetup SHA-256: {win["installerSha256"]}\n\n'
        f'<!-- clipbridge-source:{source_sha};zip:{win["sha256"]};setup:{win["installerSha256"]} -->'
    )


def public_readme(version, win):
    return (
        f'# 互传 ClipBridge\n\n当前 Windows 版本：**{version}**\n\n'
        f'[下载安装器]({win["installerUrl"]}) · [发行版与更新说明]({REPO}/releases/tag/v{version})\n\n'
        '## 更新内容\n\n'
        f'{win["notes"]}\n\n'
        '## 安装与自动更新\n\n'
        '- 0.3.8 用户需要手动安装一次 0.3.9；安装后应用会从 GitHub 获取签名更新清单，后续版本自动更新。\n'
        '- 普通用户不需要发布密钥；更新清单和安装包都会校验 SHA-256 与 RSA 签名。\n'
        '- 缺少 .NET 10 桌面运行时时，安装器从 Microsoft 官方下载、校验并请求管理员授权安装。\n'
        '- 安装器未进行 Authenticode 代码签名；Android APK 尚未发布。\n\n'
        '## 开发与发布\n\n'
        f'源码、GitHub Actions、更新清单和发行版都在 [{REPO}](https://github.com/ailuo04/clipbridge)。发布私钥只作为 GitHub Actions Secret 使用，不进入仓库、构建产物或日志。电脑重置后重新克隆该仓库即可继续开发；私钥还必须在仓库外保留加密备份。\n'
    ).encode('utf-8')


def publish(directory, version, source_sha, github):
    envelope, win = validate_bundle(directory, version)
    print(f'Validated local release {version}', flush=True)
    tag = 'v' + version
    marker = f'zip:{win["sha256"]};setup:{win["installerSha256"]}'
    release = github.release_by_tag(tag)
    if release and marker not in release.get('body', ''):
        raise PublishError('Existing release is different; never overwrite a published version.')

    zip_path = directory / f'ClipBridge-{version}-win-x64.zip'
    setup_path = directory / f'ClipBridge-{version}-win-x64-Setup.exe'
    immutable_files = {
        f'ClipBridge-{version}-win-x64.zip': zip_path.read_bytes(),
        f'release-notes-{version}.txt': (directory / 'release-notes.txt').read_bytes(),
        f'manifest-{version}.json': envelope,
    }
    for name, content in immutable_files.items():
        old = github.content(name)
        if old and base64.b64decode(old['content']) != content:
            raise PublishError('Versioned file differs; resume using the original build artifact: ' + name)
    print('Uploading immutable ZIP, notes and version manifest', flush=True)
    for name, content in immutable_files.items():
        github.write_file(name, content, immutable=True)

    if not release:
        release = github.api('/releases', 'POST', {
            'tag_name': tag,
            'target_commitish': source_sha,
            'name': f'ClipBridge / 互传 {version}',
            'body': release_body(version, win, source_sha),
            'draft': True,
            'prerelease': False,
        })

    assets = {asset['name']: asset for asset in github.assets(release)}
    for path, expected_size, expected_hash in (
        (setup_path, win['installerSize'], win['installerSha256']),
        (zip_path, win['size'], win['sha256']),
    ):
        existing = assets.get(path.name)
        if existing:
            public_bytes(existing['browser_download_url'], expected_size, expected_hash, max_bytes=100 * 1024 * 1024)
        else:
            print(f'Uploading GitHub release asset {path.name} ({path.stat().st_size} bytes)', flush=True)
            github.upload(release, path)

    body = release_body(version, win, source_sha)
    release = github.update_release(release, body, draft=False)
    print('Verifying anonymous GitHub Release asset downloads', flush=True)
    public_bytes(win['installerUrl'], win['installerSize'], win['installerSha256'], max_bytes=100 * 1024 * 1024)
    public_bytes(win['url'], win['size'], win['sha256'], max_bytes=100 * 1024 * 1024)
    print('Downloads verified; promoting the GitHub update channel', flush=True)

    github.write_file('README.md', public_readme(version, win))
    github.write_file('latest-v2.json', envelope)
    if public_bytes(RAW + '/latest-v2.json') != envelope:
        raise PublishError('Public channel is not yet serving the exact signed manifest.')
    published = github.release_by_tag(tag)
    if published.get('draft') or published.get('prerelease'):
        raise PublishError('GitHub Release is not stable after publication.')
    published_assets = {asset['name']: asset for asset in github.assets(published)}
    if setup_path.name not in published_assets or zip_path.name not in published_assets:
        raise PublishError('GitHub Release asset verification failed.')
    print(f'PASS public release {REPO}/releases/tag/{tag}', flush=True)
    print('PASS anonymous Setup/ZIP download hashes and signed manifest bytes', flush=True)
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a', encoding='utf-8') as output:
            output.write(f'## Published {version}\n\n[Download Setup]({win["installerUrl"]})\n\n[Release]({REPO}/releases/tag/{tag})\n\nSetup SHA-256: {win["installerSha256"]}\n\n{win["notes"]}\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', required=True, type=Path)
    parser.add_argument('--version', required=True)
    parser.add_argument('--source-commit', default=None)
    parser.add_argument('--publish', action='store_true')
    args = parser.parse_args()
    validate_bundle(args.directory, args.version)
    if not args.publish:
        print('PASS local artifact hashes and GitHub URLs. No remote changes; use --publish in GitHub Actions to release.')
        return
    token = os.environ.get('GITHUB_TOKEN')
    if not token:
        raise PublishError('GITHUB_TOKEN is required.')
    source_commit = args.source_commit or os.environ.get('GITHUB_SHA') or 'local'
    publish(args.directory, args.version, source_commit, GitHub(token))


if __name__ == '__main__':
    try:
        main()
    except (PublishError, OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        print('Release stopped: ' + str(error), file=sys.stderr)
        sys.exit(1)
