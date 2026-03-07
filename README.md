# PrintForm

build & run
```
dotnet build .\PrintForm\PrintForm.csproj -c Release
```
```
dotnet run --project .\PrintForm\PrintForm.csproj
```

release publish (target for installer)
```
dotnet publish .\PrintForm\PrintForm.csproj -c Release -f net8.0-windows -o .\artifacts\publish
```

build installer (inno setup)
```
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\PrintForm.iss
```

installer output
```
.\artifacts\installer\PrintForm-Setup-1.0.3.exe
```
