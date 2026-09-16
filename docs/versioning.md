# Versioning

## MinVer
The package version is derived from git tags by [MinVer](https://github.com/adamralph/minver)

| Tag                | Result                                                         |
|--------------------|----------------------------------------------------------------|
| `vX.Y.Z` on `main` | Stable release `X.Y.Z`                                         |
| `vX.Y.Z-preview.N` | Preview release `X.Y.Z-preview.N`                              |
| No tag on commit   | `X.Y.(Z+1)-preview.0.<height>`, CI artifact only, never pushed |

## Stable or preview release

Tag the commit on `main` and push the tag:

```shell
git tag v1.2.0
git push origin v1.2.0
```

## Hotfix of an older version

Branch from its tag, fix, and tag the patch version on that branch:

```shell
git checkout -b hotfix/1.1.x v1.1.0
# fix, commit
git tag v1.1.1
git push origin hotfix/1.1.x v1.1.1
```

## Workflows

- `ci.yml`: on pull requests and pushes to `main`. Restores, builds, tests, and uploads the `.nupkg` as a workflow artifact.
- `release.yml`: on `v*` tag pushes. Tests, packs with `ContinuousIntegrationBuild=true`, and pushes to nuget.org using
  [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no long-lived API key).
