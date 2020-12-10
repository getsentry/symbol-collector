# Examples

#### Build the project and upload local symbols and images
```sh
dotnet run -- --upload device --bundle-id bundle-id-name
```

#### Build the project and upload symbols and images from a specific path
```sh
dotnet run -- --upload directory --path outputs/ --batch-type android --bundle-id bundle-id-name
```

#### Build the project and run symsorter on a directory (in dryrun mode)
```sh
dotnet run -- --symsorter ../SymbolCollector.Server/dev-data/done/macos --bundle-id test-symsorter --batch-type macos --path output --dryrun true
```
