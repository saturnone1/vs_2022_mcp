<p align="center">
  <img src="https://raw.githubusercontent.com/CodingWithCalvin/VS-MCPServer/main/resources/logo.png" alt="VS MCP Server Logo" width="128" height="128">
</p>

<h1 align="center">VS MCP Server</h1>

<p align="center">
  <strong>Let AI assistants like Claude control Visual Studio through the Model Context Protocol!</strong>
</p>

<p align="center">
  <a href="https://github.com/CodingWithCalvin/VS-MCPServer/blob/main/LICENSE">
    <img src="https://img.shields.io/github/license/CodingWithCalvin/VS-MCPServer?style=for-the-badge" alt="License">
  </a>
  <a href="https://github.com/CodingWithCalvin/VS-MCPServer/actions/workflows/build.yml">
    <img src="https://img.shields.io/github/actions/workflow/status/CodingWithCalvin/VS-MCPServer/build.yml?style=for-the-badge" alt="Build Status">
  </a>
</p>

---

## Visual Studio 2022 17.12 포팅

이 저장소는 원본 VS MCP Server 프로젝트를 Visual Studio 2022 17.12에서
사용하기 위해 개인적으로 포팅한 버전입니다. 원본 프로젝트는 .NET 10 및
Visual Studio SDK 17.14 계열 패키지 기준으로 이동해 있었고, 이 상태에서는
Visual Studio 2022 17.12 환경에서 빌드하거나 확장을 로드할 때 문제가
발생했습니다.

### 작업 내용

- 서버 프로젝트와 공유 라이브러리 타깃을 `net10.0`에서 `net9.0`으로
  변경했습니다.
- 로컬 빌드가 .NET 9 SDK 계열을 사용하도록 `global.json`을 추가했습니다.
- Visual Studio SDK 관련 패키지를 17.12 계열로 고정했습니다.
- VSIX manifest의 설치 대상을 Visual Studio 2022 17.12 이상, VS 2022 범위로
  제한했습니다.
- SDK 스타일 VSIX 프로젝트가 이 환경에서 `.vsix` 파일을 생성하지 않아,
  명시적인 VSIX 패키징 타깃을 추가했습니다.
- Visual Studio SDK의 `VsixUtil` 도구로 기본 VSIX 패키지를 만든 뒤, 확장
  payload를 안정적인 경로로 주입하도록 패키징 스크립트를 추가했습니다.
- Visual Studio 2022 내부 어셈블리와 충돌하지 않도록 VSIX payload에서
  Visual Studio가 제공하는 어셈블리(`Microsoft.VisualStudio.*`, `EnvDTE*`,
  `stdole.*`, `VSLangProj*`)를 제외했습니다.
- Visual Studio 2022 17.12 로드 호환성을 위해 외부
  `CodingWithCalvin.Otel4Vsix`/OpenTelemetry 의존성을 제거하고, 기존 호출은
  no-op telemetry shim으로 대체했습니다.
- 패키지 로드 문제를 추적하기 쉽도록
  `%LOCALAPPDATA%\VS-MCPServer\extension-load.log`에 초기화 로그를 남기도록
  했습니다.
- VSIX 버전을 `1.0.2`로 갱신했습니다.

### 빌드 방법

다음 명령으로 빌드합니다.

```powershell
dotnet build src\CodingWithCalvin.MCPServer.slnx -c Release -m:1
```

빌드가 끝나면 VSIX는 다음 위치에 생성됩니다.

```text
src\CodingWithCalvin.MCPServer\bin\Release\VS-MCPServer.vsix
```

설치 또는 로드 문제를 테스트할 때는 기존에 설치된 MCP Server 확장을 제거하고,
Visual Studio를 완전히 종료한 뒤 다시 설치하는 것을 권장합니다. 필요한 경우
Visual Studio component model cache도 삭제하세요.

## 🤔 What is this?

**VS MCP Server** exposes Visual Studio features through the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/), enabling AI assistants like Claude to interact with your IDE programmatically. Open files, read code, build projects, and more - all through natural conversation!

## ✨ Features

### 📂 Solution Tools

| Tool | Description |
|------|-------------|
| `project_info` | Get detailed project information |
| `project_list` | List all projects in the solution |
| `solution_close` | Close the current solution |
| `solution_info` | Get information about the current solution |
| `solution_open` | Open a solution file |
| `startup_project_get` | Get the current startup project |
| `startup_project_set` | Set the startup project for debugging |

### 📝 Document Tools

| Tool | Description |
|------|-------------|
| `document_active` | Get the active document |
| `document_cleanup` | Run code cleanup on a document |
| `document_close` | Close a document |
| `document_list` | List all open documents |
| `document_open` | Open a file in the editor |
| `document_read` | Read document contents |
| `document_save` | Saves an open document |
| `document_write` | Write to a document |

### ✏️ Editor Tools

| Tool | Description |
|------|-------------|
| `editor_find` | Search within documents |
| `editor_goto_line` | Navigate to a specific line |
| `editor_insert` | Insert text at cursor position |
| `editor_replace` | Find and replace text |
| `selection_get` | Get the current text selection |
| `selection_set` | Set the selection range |

### 🔨 Build Tools

| Tool | Description |
|------|-------------|
| `build_cancel` | Cancel a running build |
| `build_project` | Build a specific project |
| `build_solution` | Build the entire solution |
| `build_status` | Get current build status |
| `clean_solution` | Clean the solution |

### 🧭 Navigation Tools

| Tool | Description |
|------|-------------|
| `find_references` | Find all references to a symbol |
| `goto_definition` | Navigate to the definition of a symbol |
| `symbol_document` | Get all symbols defined in a document |
| `symbol_workspace` | Search for symbols across the solution |

### 🐛 Debugger Tools

| Tool | Description |
|------|-------------|
| `debugger_add_breakpoint` | Add a breakpoint at a file and line |
| `debugger_break` | Pause execution (Ctrl+Alt+Break) |
| `debugger_continue` | Continue execution (F5) |
| `debugger_evaluate` | Evaluate an expression in the current debug context |
| `debugger_get_callstack` | Get the call stack |
| `debugger_get_locals` | Get local variables in current frame |
| `debugger_launch` | Start debugging (F5), optionally for a specific project |
| `debugger_launch_without_debugging` | Start without debugger (Ctrl+F5), optionally for a specific project |
| `debugger_list_breakpoints` | List all breakpoints |
| `debugger_remove_breakpoint` | Remove a breakpoint |
| `debugger_set_variable` | Set the value of a local variable |
| `debugger_status` | Get current debugger state |
| `debugger_step_into` | Step into (F11) |
| `debugger_step_out` | Step out (Shift+F11) |
| `debugger_step_over` | Step over (F10) |
| `debugger_stop` | Stop debugging (Shift+F5) |

### 🔍 Diagnostics Tools

| Tool | Description |
|------|-------------|
| `errors_list` | Read build errors, warnings, and messages from the Error List |
| `output_list_panes` | List all available Output window panes |
| `output_read` | Read content from an Output window pane |
| `output_write` | Write a message to an Output window pane |

### 🪟 Window Tools

| Tool | Description |
|------|-------------|
| `toolwindow_hide` | Hide (close) a tool window by caption |
| `toolwindow_show` | Show a tool window by name (SolutionExplorer, ErrorList, Output, Terminal, etc.) |
| `window_activate` | Activate (focus) a window by caption |
| `window_list` | List all open windows with caption, kind, visibility, and GUID |

## 🛠️ Installation

### Visual Studio Marketplace

1. Open Visual Studio 2022 17.12 or later
2. Go to **Extensions > Manage Extensions**
3. Search for "MCP Server"
4. Click **Download** and restart Visual Studio

### Manual Installation

Download the latest `.vsix` from the [Releases](https://github.com/CodingWithCalvin/VS-MCPServer/releases) page and double-click to install.

## 🚀 Usage

### ▶️ Starting the Server

1. Open Visual Studio
2. Go to **Tools > MCP Server > Start Server** (or enable auto-start in settings)
3. The MCP server starts on `http://localhost:5050`

### 🤖 Configuring Claude Desktop & Claude Code

Add this to your Claude Desktop or Claude Code MCP settings (preferred HTTP method):

```json
{
  "mcpServers": {
    "visualstudio": {
      "type": "http",
      "url": "http://localhost:5050"
    }
  }
}
```

**Legacy SSE method** (deprecated, but still supported):

```json
{
  "mcpServers": {
    "visualstudio": {
      "type": "sse",
      "url": "http://localhost:5050/sse"
    }
  }
}
```

> ℹ️ **Note:** The HTTP method is the preferred standard. SSE (Server-Sent Events) is a legacy protocol and should only be used for backward compatibility.

### ⚙️ Settings

Configure the extension at **Tools > Options > MCP Server**:

| Setting | Description | Default |
|---------|-------------|---------|
| Auto-start server | Start the MCP server when Visual Studio launches | Off |
| Binding Address | Address the server binds to | `localhost` |
| HTTP Port | Port for the MCP server | `5050` |
| Server Name | Name reported to MCP clients | `Visual Studio MCP` |
| Log Level | Minimum log level for output | `Information` |
| Log Retention | Days to keep log files | `7` |

## 🏗️ Architecture

```
+------------------+              +----------------------+   named pipes   +------------------+
|  Claude Desktop  |   HTTP/SSE  |  MCPServer.Server    | <-------------> |  VS Extension    |
|  (MCP Client)    | <---------> |  (MCP Server)        |    JSON-RPC     |  (Tool Impl)     |
+------------------+    :5050    +----------------------+                 +------------------+
```

## 🤝 Contributing

Contributions are welcome! Whether it's bug reports, feature requests, or pull requests - all feedback helps make this extension better.

### 🔧 Development Setup

1. Clone the repository
2. Open `src/CodingWithCalvin.MCPServer.slnx` in Visual Studio 2022
3. Ensure you have the "Visual Studio extension development" workload installed
4. Ensure you have the .NET 9 SDK installed
5. Press F5 to launch the experimental instance

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## 👥 Contributors

<!-- readme: contributors -start -->
<a href="https://github.com/CalvinAllen"><img src="https://avatars.githubusercontent.com/u/41448698?v=4&s=64" width="64" height="64" align="left" alt="CalvinAllen"></a> <a href="https://github.com/Gh61"><img src="https://avatars.githubusercontent.com/u/10837736?v=4&s=64" width="64" height="64" align="left" alt="Gh61"></a> <a href="https://github.com/laviRZ"><img src="https://avatars.githubusercontent.com/u/29277997?v=4&s=64" width="64" height="64" align="left" alt="laviRZ"></a> <a href="https://github.com/shaiku"><img src="https://avatars.githubusercontent.com/u/16620522?v=4&s=64" width="64" height="64" align="left" alt="shaiku"></a> <br clear="all">
<!-- readme: contributors -end -->

---

<p align="center">
  Made with ❤️ by <a href="https://github.com/CodingWithCalvin">Coding With Calvin</a>
</p>
