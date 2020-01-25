# Examples

#### Build the project and upload local symbols and images
```sh
dotnet run -- --upload device --bundle-id bundle-id-name
```

#### Build the project and run symsorter on a directory (in dryrun mode)
```sh
ddotnet run -- --symsorter ../SymbolCollector.Server/dev-data/done/macos --bundle-id test-symsorter --batch-type macos --path output --dryrun true
```
