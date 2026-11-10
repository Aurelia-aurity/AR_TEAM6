# AR_TEAM6 多人协作与 GitHub 提交规范

本文件是仓库级 LLM 指令。每次新对话开始处理代码前，必须先阅读本文件，并按以下多人协作流程执行。

## 1. 对话开始时的仓库检查

开始修改前必须检查：

```powershell
git status --short --branch
git remote -v
git log --oneline -5
```

- 确认当前分支、远程地址和工作区状态。
- 先同步远程最新提交：`git pull --rebase origin <当前分支>`。
- 若工作区存在不属于本任务的修改，禁止覆盖、回滚或清理；应保留并避开这些文件。
- 若发现冲突、未提交的关键工作或远程历史异常，先停止并向用户说明。

## 2. 分支策略

- 每个独立功能使用独立分支，推荐命名：
  - `feature/<功能名>`
  - `fix/<问题名>`
  - `perf/<优化名>`
  - `docs/<文档名>`
- 不要直接在 `main` 上进行持续开发。
- 修改前从最新远程目标分支创建分支：

```powershell
git switch -c feature/<功能名>
```

- 分支已存在时，先确认归属和状态，不得擅自删除或重置。

## 3. 提交粒度

- 每完成一个完整、可验证的功能或修复，至少创建一个 Git commit。
- 一个 commit 只包含一个逻辑目的；不要把无关格式化、临时文件或其他人的修改混入。
- 不要求每改一行就提交，但不得让多个无关功能长期堆积在同一个 commit。
- 提交前必须检查：

```powershell
git diff --check
git status --short
git diff --stat
```

- 只暂存本次任务相关文件，优先使用明确路径的 `git add`，不要盲目使用 `git add .`。

## 4. 提交信息

提交信息使用 Conventional Commits：

```text
feat: add calibration state machine
fix: prevent duplicate hit events
perf: use async GPU readback
docs: update collaboration guide
test: add calibration coverage
```

每次创建 commit 都必须包含：

```text
Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
```

不要 amend 他人提交，不要改写公共分支历史，不要使用 `git reset --hard`、`git checkout --` 等破坏性命令。

## 5. 验证与推送

- 提交前运行覆盖本次改动的最小构建、测试或静态检查。
- Unity 代码改动至少执行 `git diff --check`；如果本机有 Unity Editor，再执行对应 Unity batch 编译或测试。
- 无法运行 Unity 或真机测试时，必须在最终回复明确说明未验证项。
- 验证通过后立即推送当前功能分支：

```powershell
git push -u origin <当前分支>
```

- 推送后确认：

```powershell
git status --short --branch
git ls-remote origin refs/heads/<当前分支>
```

- 不得强制推送。不得把密钥、证书、个人配置、Unity 生成目录或构建产物提交到仓库。

## 6. Pull Request 协作

- 功能分支完成后创建 Pull Request，不直接把未经审查的功能合并到 `main`。
- PR 描述必须包含：
  - 功能目的和改动范围
  - 关键文件
  - 已执行的验证
  - 尚未完成的真机、性能或集成测试
- 处理冲突时以远程最新代码为基础，保留其他成员的有效改动；无法判断时停止并询问用户。

## 7. 最终回复格式

每次完成代码任务后，最终回复必须简要说明：

1. 完成了什么；
2. 生成的 commit hash 和分支；
3. 是否已推送到 GitHub；
4. 执行了哪些验证；
5. 哪些验证因环境限制未执行。

如果用户明确要求只分析、只给方案或不修改代码，则不创建 commit、不推送，并说明原因。
