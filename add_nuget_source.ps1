Invoke-Command { 
    nuget sources remove -Name "Avalonia Nightly"
    nuget sources add -Name "Avalonia Nightly" -Source "https://www.myget.org/F/avalonia-ci/api/v2" -NonInteractive 
} -ErrorAction SilentlyContinue
