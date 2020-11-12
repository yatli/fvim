namespace FVim

open ui
open log
open common

type CrashReportViewModel(ex: exn, code: int, msgs: ResizeArray<string>) =
    inherit ViewModelBase()
    member __.MainMessage =
        sprintf "Exit code: %d\n" code + 
        sprintf "Exception message: %s\n" ex.Message + 
        join msgs
    member __.StackTrace =
        ex.StackTrace
    member __.TipMessage = 
        let tip = 
            match ex.Message.Trim() with
            | "The system cannot find the file specified." 
            | "No such file or directory" -> "Tip: check your neovim installation. `nvim` is not in your $PATH.\n"
            | "neovim crashed" -> "The neovim process exited abnormally.\n"
            | _ -> ""
        let generic_message = 
               "You can go to https://github.com/yatli/fvim/issues, and search\n" + 
               "for relevant issues with the exception message, or the stack trace.\n" +
               "Feel free to create new issues, and I'll help to triage and fix the problem."
        tip + generic_message

