# Examples

#### Build the project and upload local symbols and images (looks for symbols on different system directories like /bin)
```sh
dotnet run -- --upload device --bundle-id bundle-id-name  --server-endpoint https://symbol-collector.services
```

#### Build the project and upload symbols and images from a specific path (useful to upload output of a compilation process)
```sh
dotnet run -- --upload directory --path outputs/ --batch-type android --bundle-id bundle-id-name --server-endpoint https://symbol-collector.services
```

#### Build the project and run symsorter on a directory (in dryrun mode)
```sh
dotnet run -- --symsorter ../SymbolCollector.Server/dev-data/done/macos --bundle-id test-symsorter --batch-type macos --path output --dryrun true
```

Note that this CLI is [used by `craft`](https://github.com/getsentry/craft) through the [`symbol-collector` target](https://github.com/getsentry/craft/pull/266).
