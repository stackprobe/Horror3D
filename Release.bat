CALL Clean.bat

Powershell -ExecutionPolicy Bypass -File Release.ps1

C:\app\Annex\SubTools\Dev\RevisionCodeGen.exe * out

TIMEOUT 2
