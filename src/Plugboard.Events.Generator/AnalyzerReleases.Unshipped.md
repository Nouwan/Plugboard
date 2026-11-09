; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

| Rule ID | Category  | Severity | Notes                                              |
|---------|-----------|----------|----------------------------------------------------|
| PB201   | Plugboard | Error    | Subscribe method must have a valid signature       |
| PB203   | Plugboard | Error    | Event names must be unique                         |
| PB204   | Plugboard | Error    | Event types must be non-generic and non-abstract   |
| PB205   | Plugboard | Error    | Event handler must have a valid signature          |