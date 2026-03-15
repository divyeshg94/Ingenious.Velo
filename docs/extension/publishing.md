# Publishing to Visual Studio Marketplace

## Prerequisites

1. Publisher account at `marketplace.visualstudio.com/manage` — publisher ID: `divyeshg94`
2. Personal Access Token with **Marketplace (Publish)** scope
3. `tfx-cli` installed: `npm install -g tfx-cli`

## Package the Extension

```bash
cd src/Velo.Extension
npm run build          # production Angular build
npm run package        # creates velo-{version}.vsix
```

## Publish (Private Preview)

```bash
tfx extension publish \
  --manifest-globs vss-extension.json \
  --token <YOUR_PAT> \
  --share-with <your-ado-org>
```

## Publish (Public)

Update `vss-extension.json` → `"public": true`, then:

```bash
tfx extension publish --manifest-globs vss-extension.json --token <YOUR_PAT>
```

## Versioning

The extension version in `vss-extension.json` must be incremented for each publish. Use semantic versioning: `MAJOR.MINOR.PATCH`.

The CI/CD pipeline (`cd-prod.yml`) automates publishing on release tags (`v*.*.*`).
