"""Offline synthetic tests: no credentials, network or real release mutations."""
import base64
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import publish_github as pub


class FakeGitHub:
    def __init__(self):
        self.files = {}
        self.release = None
        self.asset_names = []
        self.events = []

    def content(self, name):
        if name not in self.files:
            return None
        return {'content': base64.b64encode(self.files[name]).decode(), 'sha': 'synthetic', 'commit': {'sha': 'synthetic'}}

    def write_file(self, name, content, immutable=False):
        if self.files.get(name) == content:
            return 'synthetic-commit'
        if immutable and name in self.files:
            raise pub.PublishError('Immutable file exists')
        self.events.append(name)
        self.files[name] = content
        return 'synthetic-commit'

    def release_by_tag(self, tag):
        return self.release

    def assets(self, release):
        return [{'name': name, 'browser_download_url': 'https://synthetic.invalid/' + name} for name in self.asset_names]

    def upload(self, release, path):
        self.asset_names.append(path.name)
        self.events.append('asset:' + path.name)
        return {'name': path.name}

    def api(self, path, method='GET', data=None, missing=False):
        if path == '/releases' and method == 'POST':
            self.release = dict(data, id=123, upload_url='https://synthetic.invalid/upload{?name}')
            return self.release
        raise AssertionError((path, method))

    def update_release(self, release, body, draft=False):
        self.release.update(body=body, draft=draft, prerelease=False)
        return self.release


class PublishTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='clipbridge-publisher-synthetic-')
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.version = '1.2.3'
        zip_data, setup_data = b'isolated synthetic zip', b'isolated synthetic setup'
        self.win = {
            'version': self.version,
            'notes': 'Synthetic test, not a real release.',
            'size': len(zip_data),
            'sha256': pub.sha256(zip_data),
            'url': pub.RAW + '/ClipBridge-1.2.3-win-x64.zip',
            'installerSize': len(setup_data),
            'installerSha256': pub.sha256(setup_data),
            'installerUrl': pub.REPO + '/releases/download/v1.2.3/ClipBridge-1.2.3-win-x64-Setup.exe',
        }
        self.envelope = json.dumps({
            'payload': base64.b64encode(json.dumps({'schema': 1, 'windows': self.win}).encode()).decode(),
            'signature': 'synthetic-not-a-real-signature',
        }).encode()
        for name, content in {
            'latest-v2.json': self.envelope,
            'manifest-1.2.3.json': self.envelope,
            'release-notes.txt': self.win['notes'].encode(),
            'ClipBridge-1.2.3-win-x64.zip': zip_data,
            'ClipBridge-1.2.3-win-x64-Setup.exe': setup_data,
        }.items():
            (self.directory / name).write_bytes(content)
        self.github = FakeGitHub()

    def public(self, url, *args, **kwargs):
        if url.endswith('/latest-v2.json'):
            return self.envelope
        return None

    def publish(self):
        with patch.object(pub, 'public_bytes', side_effect=self.public):
            pub.publish(self.directory, self.version, 'synthetic-source', self.github)

    def test_publish_channel_last_and_release_is_stable(self):
        self.publish()
        self.assertEqual(self.github.events[-1], 'latest-v2.json')
        self.assertIn('asset:ClipBridge-1.2.3-win-x64-Setup.exe', self.github.events)
        self.assertFalse(self.github.release['draft'])
        self.assertFalse(self.github.release['prerelease'])

    def test_retry_same_artifacts_is_idempotent(self):
        self.publish()
        first = list(self.github.events)
        self.publish()
        self.assertEqual(self.github.events, first)

    def test_failed_download_does_not_promote_channel(self):
        with patch.object(pub, 'public_bytes', side_effect=pub.PublishError('Synthetic availability failure')):
            with self.assertRaises(pub.PublishError):
                pub.publish(self.directory, self.version, 'synthetic-source', self.github)
        self.assertNotIn('latest-v2.json', self.github.files)
        self.assertFalse(self.github.release['draft'])

    def test_different_existing_version_stops_before_writes(self):
        self.github.files['manifest-1.2.3.json'] = b'previous synthetic build'
        with self.assertRaises(pub.PublishError):
            self.publish()
        self.assertEqual(self.github.events, [])

    def test_release_note_line_ending_difference_is_compatible(self):
        self.github.files['release-notes-1.2.3.txt'] = self.win['notes'].encode() + b'\r\n'
        self.publish()
        self.assertNotIn('release-notes-1.2.3.txt', self.github.events)

    def test_version_order_and_validation(self):
        self.assertGreater(pub.version_tuple('1.10.0'), pub.version_tuple('1.9.9'))
        with self.assertRaises(pub.PublishError):
            pub.version_tuple('01.2.3')

    def test_public_readme_documents_elevated_install_and_non_elevated_updates(self):
        readme = pub.public_readme('0.3.10', {
            'installerUrl': 'https://example.invalid/setup.exe',
            'notes': 'Installer policy test',
        }).decode('utf-8')
        self.assertIn('管理员权限', readme)
        self.assertIn('后续更新无需重复 UAC', readme)


if __name__ == '__main__':
    unittest.main()
