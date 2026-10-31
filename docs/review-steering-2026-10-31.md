# “补充当前任务”专项验证（2026-10-31）

当前 Lovest Code 0.2.2 的纯文字补充，在本次隔离验证中通过。生产 APK 和服务端业务实现未修改，本次增加可重复运行的专项测试。

## 实际执行

| 检查 | 结果 |
| --- | --- |
| Android 14，正式 0.2.2 APK | 22 项通过，包含登录与原生桥基础检查。 |
| Desktop IPC / CLI 专项 | 七项通过。 |
| 既有发送结果恢复回归 | 七项通过。 |
| 既有侧边聊天补充回归 | 一项通过。 |
| 固定上游完整性 | 1,264 个文件与七项覆盖匹配。 |

安卓测试实际触摸生产发送按钮，连续补充三条中文消息。请求经过生产登录、HTTP 路由、Bridge 发送逻辑和 Windows 命名管道，终点为临时 Desktop owner 夹具。没有拦截 `/send` 返回合成确认；测试脚本仅记录请求和响应。

每次请求均使用 `mode=steer`、`workMode=null`；原文和消息 UUID 与 `thread-follower-steer-turn` 完全一致，路由保持原聊天与 owner。原生夹具回传的三条 `steeringUserMessage` 均属于同一个仍运行中的回合，没有调用新建回合、停止或切换设置。成功后清除已提交文本，键盘与输入区仍可用。

模拟任务在发送前结束：HTTP 返回 400 / `send_not_submitted`，界面保留草稿并提示使用普通发送；状态刷新后禁用补充选项，未为拒绝的消息记录提交或另开任务。协议测试另覆盖相同消息去重、未知结果不重放和禁止在补充时切换工作模式。

CLI 协议验证绑定 `expectedTurnId`，保留原 provider 和工作目录；当前 CLI 补充接口明确拒绝文件附件。纯文字补充通过，不将此结果扩大为附件支持。

## 复现

```powershell
python -B tests/mobile/test_steering.py
python -B tests/android/run-smoke.py --jdk C:/path/to/jdk-17 --sdk C:/path/to/Android/Sdk --apk artifacts/LovestCode-Android-0.2.2.apk --mode steer
```

安卓命令需要已启动的隔离模拟器（默认 `emulator-5562`）及既有签名目录。夹具控制接口仅由测试代理提供，未进入生产网关；测试脚本仅放入独立测试 APK。

结果位于本机忽略目录 `artifacts/android-steering-smoke/report.json`、`artifacts/steering-protocol-regression.log`，截图包括 `steer-accepted.png` 和 `steer-finished-draft.png`。目标正式 APK SHA-256 仍为 `da9ca97615a03f109085d0d6238aaf563f80762f0d71c649be1f8228eaedd0a4`。

## 边界

此次原生 owner 与 CLI 接口使用隔离夹具，没有向真实 Codex 模型或正在工作的用户聊天发送消息。因此确认的是当前 APK、生产网关和协议转发的行为；实际模型何时采纳补充，以及特定 Codex Desktop 版本的运行时兼容性，仍需对应真实会话验证。
