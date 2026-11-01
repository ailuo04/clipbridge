# ClipBridge

ClipBridge is a Windows clipboard bridge with signed GitHub releases and automatic updates.

## Repositories and releases

The single source and release repository is [github.com/ailuo04/clipbridge](https://github.com/ailuo04/clipbridge). Future releases use GitHub only. The signed update channel is [latest-v2.json](https://raw.githubusercontent.com/ailuo04/clipbridge/master/latest-v2.json).

## Migration

Versions 0.3.8 and older still point to the legacy update endpoint. Users on 0.3.8 must manually install 0.3.9 once. Version 0.3.9 embeds the GitHub update endpoint and the new signing public key; later versions continue with automatic GitHub updates.

## Recovery after a PC reset

Clone this repository to restore the source, build scripts, tests and GitHub Actions. The update private key cannot be reconstructed from the public key. Keep two encrypted backups outside the repository and configure the key as the protected GitHub Actions Secret CLIPBRIDGE_UPDATE_PRIVATE_KEY. Normal users never need the release key.
