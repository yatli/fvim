namespace FVim

type CrashReportSampleData() =
    member __.MainMessage = "Some error: some detailed error message.\nThis error is so bad that we have to bail out."
    member __.TipMessage = "\n\nIn case of fire,\n    1. git commit\n    2. git push\n    3. run\nThank you for your cooperation."
    member __.StackTrace = System.Collections.Generic.List [
        "foo"
        "bar"
        "baz"
    ] 

