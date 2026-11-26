# XNotInclude PowerShell 协作指令说明（给 AI 使用）

## 0. 运行环境与交互协议

- 操作系统：Windows，PowerShell 5.1。
- 指令脚本：`E:\wsp\XNotInclude\dev-tools.ps1`。
- 已写入当前用户 Profile：`C:\Users\10670\Documents\WindowsPowerShell\profile.ps1`，新开 PowerShell 自动加载。
- Profile 已强制移除 `write` / `move` 内置别名，避免覆盖自定义函数。
- 默认项目根目录：`E:\wsp\XNotInclude`。
- 可用 `$global:XNotIncludeDevRoot = '<路径>'` 临时切换根目录。

### AI 生成命令时必须遵守的交互协议

1. 所有操作都必须包在 `RUN { ... }` 里。
2. 用户把 `RUN { ... }` 粘贴到 PowerShell 回车。
3. 指令的全部 stdout/stderr 会被合并写入剪贴板，终端只显示一行摘要：`[OK] 已复制 N 行 / X KB`。
4. 用户切回聊天窗口 `Ctrl+V`，把剪贴板内容贴回来给 AI 分析。
5. 如果块内任意一步报错，`RUN` 会停止并把错误文本一起复制进剪贴板，AI 应先处理错误。

### 通用硬约束

- 所有路径必须是**项目根目录下的相对路径**，正斜杠 `/` 和反斜杠 `\` 都支持。
- 禁止绝对路径；路径解析后必须落在项目根目录内，否则指令抛错。
- 传代码/片段时一律使用 PowerShell 单引号 here-string：`@' ... '@`，不要用双引号 here-string，也不要自己转义 `$"`、`\`、反引号等字符。
- `PATCH` 依赖换行归一化：从聊天复制的 CRLF/LF 差异会被内部抹平，匹配按 `\n` 做，写回时恢复目标文件原本的换行风格。
- 函数名大小写不敏感，但 AI 输出时请统一使用大写形式。

---

## 1. `RUN` —— 批量执行与剪贴板汇总

```powershell
RUN {
    <任意多条指令>
}
```

- 参数：`[scriptblock]$Block`，必填。
- 行为：
  - 设置 `$ErrorActionPreference = 'Stop'`，遇错即停。
  - 捕获块内所有输出流（含 native 命令 stderr）。
  - 一次性 `Set-Clipboard`。
  - 终端仅打印摘要行。
- 示例：

```powershell
RUN {
    READ Assets/Scripts/Core/ElementDef.cs
    GREP "TimeScale" Assets/Scripts -C 2
    GIT status
}
```

---

## 2. `TREE` —— 递归列文件

```powershell
TREE [路径]
```

- 参数：
  - `[string]$Path`：默认 `'.'`，相对项目根目录。
- 输出：每个文件一行 `相对路径 (N lines)`；二进制或读取失败时显示 `(N bytes)`。
- 默认过滤：`.meta`、`Library`、`Temp`、`obj`、`Logs`、`.git`、`.idea`、`UserSettings`。
- 示例：

```powershell
RUN { TREE Assets/Scripts }
```

---

## 3. `READ` —— 读文件（支持多文件与行号范围）

```powershell
READ 路径[:起-止] ...
```

- 参数：`[string[]]$Specs`，剩余参数，必填。
- 规格格式：`相对路径[:起始行[-结束行]]`。
  - 只写路径：读全文。
  - `:10-80`：读第 10 到 80 行。
  - `:10`：从 10 行读到文件末尾。
- 输出：
  - 文件头：`// === 相对路径 (起-止 / 总行数) ===`
  - 行内容：`   1: 代码内容`，1-based。
  - 多个文件之间空一行。
- 示例：

```powershell
RUN {
    READ Assets/Scripts/Core/ElementDef.cs
    READ Assets/Scripts/Runtime/SimBootstrap.cs:100-140
}
```

---

## 4. `GREP` —— 搜索代码文本/正则

```powershell
GREP 关键词 [路径] [-C 上下文行数]
```

- 参数：
  - `[string]$Pattern`：必填，正则或普通文本。
  - `[string]$Path`：可选，默认 `Assets/Scripts`。
  - `[Alias('C')][int]$Context`：可选，默认 `0`。
- 搜索范围：仅 `.cs`、`.shader`、`.hlsl`、`.asmdef`、`.json`。
- 输出格式：`相对路径:行号: 内容`。
  - 有 `-C` 时，上下文行使用 `路径:行号- 内容` / `路径:行号+ 内容`。
- 上限：最多 100 条匹配；超限会先输出 `[GREP] 共 N 条，仅显示前 100 条`。
- 示例：

```powershell
RUN {
    GREP "TimeScale" Assets/Scripts -C 2
    GREP "error CS" .
}
```

---

## 5. `WRITE` —— 新建/整文件覆写

```powershell
WRITE 路径 @'
完整代码
'@
```

- 参数：
  - `[string]$Path`：必填，相对路径。
  - `[string]$Content`：必填，必须用单引号 here-string 传入。
- 行为：
  - 父目录不存在自动创建。
  - 统一写 UTF-8 **无 BOM**。
  - 去掉内容开头多余空行。
- 成功输出：`[WRITE OK] 相对路径 (N lines)`。
- 示例：

```powershell
RUN {
    WRITE Assets/Scripts/Core/Demo.cs @'
public static class Demo
{
    public const int Value = 1;
}
'@
}
```

---

## 6. `PATCH` —— 精准局部替换

```powershell
PATCH 路径 @'
旧片段
'@ @'
新片段
'@
```

- 参数：
  - `[string]$Path`：必填。
  - `[string]$Old`：必填，旧片段。
  - `[string]$New`：必填，新片段。
- 核心安全规则：
  - 旧片段必须在文件中**精确匹配 1 次**。
  - 命中 0 次或大于 1 次：直接抛错并中止，输出命中次数。
  - 不使用行号定位，避免同一批次连续改同一文件时行号漂移。
- 换行与编码：
  - 比对时把文件与片段都归一化成 `\n`。
  - 替换后按原文件换行风格写回（CRLF 或 LF）。
  - 保留原文件是否有 UTF-8 BOM。
- 成功输出：`[PATCH OK] 相对路径 (N lines)`。
- 示例：

```powershell
RUN {
    PATCH Assets/Scripts/Runtime/SimBootstrap.cs @'
    private const int MaxTickBudget = 8192;
'@ @'
    private const int MaxTickBudget = 4096;
'@
}
```

---

## 7. `MOVE` —— Unity 安全移动/重命名

```powershell
MOVE 旧路径 新路径
```

- 参数：`[string]$OldPath`、`[string]$NewPath`，均为相对路径。
- 行为：
  - 目标已存在则拒绝覆盖。
  - 移动文件时，如同级存在 `旧路径.meta`，同步移动为 `新路径.meta`，避免 Unity GUID 丢失。
  - 自动创建目标父目录。
- 成功输出：`[MOVE OK] 旧 -> 新 (+meta)`（有 meta 时带 `(+meta)`）。

---

## 8. `DELETE` —— Unity 安全删除

```powershell
DELETE 路径
```

- 参数：`[string]$Path`，相对路径，文件或目录均可。
- 行为：
  - 删除目标文件/目录。
  - 若存在同路径 `.meta` 或同名目录的 `.meta`，一并删除。
- 成功输出：`[DELETE OK] 路径 (dir) (+meta)`（按实际情况附加）。

---

## 9. `GIT` —— 精简 Git 操作

```powershell
GIT status
GIT diff [路径]
GIT restore 路径
```

- 仅支持三种子命令，其它一律拒绝。
- `status`：输出 `git status -s`。
- `diff`：带路径时只输出该文件差异；不带路径输出全部差异。
- `restore`：还原指定文件到 HEAD。
- 内部显式调用 `git.exe`，避免与 `GIT` 函数递归冲突。
- 示例：

```powershell
RUN {
    GIT status
    GIT diff Assets/Scripts/Core/ElementRegistry.cs
}
```

---

## 10. `DIAG` —— 编译/异常诊断

```powershell
DIAG unity
DIAG build
```

- 参数：`[ValidateSet('unity','build')][string]$Mode`，默认 `unity`。
- `unity`：
  - 读取 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 尾部 800 行。
  - 过滤 `error CS\d{4}`、`Exception`、`BuildFailedException`、`Compilation failed`。
  - 输出最多 80 条匹配行；无匹配输出提示。
- `build`：
  - 在项目根目录找到 `Assembly-CSharp.csproj`。
  - 执行 `dotnet build Assembly-CSharp.csproj --no-restore --nologo -v q`。
  - 用于快速抓 C# 语法/类型错误，无需切回 Unity 等重编译。

---

## 11. AI 输出格式建议

给用户的回复里，只输出一个 PowerShell 代码块，例如：

````powershell
RUN {
    READ Assets/Scripts/Core/ElementDef.cs:1-60
    GREP "IsInsulator" Assets/Scripts -C 2
    PATCH Assets/Scripts/Core/ElementRegistry.cs @'
            TC = 0f,
'@ @'
            TC = 0.00001f,
'@
    GIT diff Assets/Scripts/Core/ElementRegistry.cs
}
````

不要发明新指令名，不要省略 `RUN`，不要把多个独立的写入操作拆成多次让用户粘贴。所有输出都通过剪贴板一次性回传。
