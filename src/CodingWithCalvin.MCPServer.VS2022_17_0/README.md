# Visual Studio 2022 17.0 VSIX

This project builds a VSIX for Visual Studio 2022 versions `17.0` through `17.11`.
The main `CodingWithCalvin.MCPServer` project remains the VS 2022 `17.12+` build.

Build this compatibility VSIX with .NET Framework MSBuild from Visual Studio:

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe
& $msbuild `
  src\CodingWithCalvin.MCPServer.VS2022_17_0\CodingWithCalvin.MCPServer.VS2022_17_0.csproj `
  /t:Restore,Build /p:Configuration=Release /m:1
```

Output:

```text
src\CodingWithCalvin.MCPServer.VS2022_17_0\bin\Release\VS-MCPServer-VS2022-17.0.vsix
```
