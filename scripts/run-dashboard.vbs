Set WshShell = CreateObject("WScript.Shell")
scriptDir = SplitPath(WScript.ScriptFullName)
exePath = scriptDir & "\..\bin\Release\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe"
WshShell.Run """" & exePath & """", 1, False

Function SplitPath(fullPath)
    SplitPath = Left(fullPath, InStrRev(fullPath, "\") - 1)
End Function
