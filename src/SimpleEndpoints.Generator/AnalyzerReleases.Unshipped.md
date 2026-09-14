; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

| Rule ID | Category        | Severity | Notes                                                   |
|---------|-----------------|----------|---------------------------------------------------------|
| SE001   | SimpleEndpoints | Error    | Endpoint must declare MapEndpoint                       |
| SE002   | SimpleEndpoints | Error    | Endpoint group must declare Configure                   |
| SE003   | SimpleEndpoints | Error    | Endpoint group type must be marked with [EndpointGroup] |
| SE004   | SimpleEndpoints | Error    | Endpoint types must be non-generic and non-abstract     |
| SE005   | SimpleEndpoints | Error    | Endpoint group names must be unique                     |


