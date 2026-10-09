# W2 任务3 · token 计费 / 上下文 / 什么该塞什么不该（含实测）

> 日期：2026-10-09
> 端点：`https://maas.qianwenaiapi.com/compatible-mode/v1`（OpenAI 兼容中转）
> 模型：`qwen3.8-max`（推理模型）
> 本篇的 token 数字均为**真调 API 实测**，非估算。

---

## 关键点：token 是"钱 + 容量"的双重单位

token 同时决定①花多少钱 ②一次能塞多少，两件事共用同一度量 → 省 token = 省钱 + 腾容量。

## 一、计费分三块，推理模型有第三块隐形账单

| 类型 | 是什么 | 注意 |
|---|---|---|
| input(prompt) | 发过去的 prompt（系统提示+few-shot+塞的代码/文档） | 便宜，但多轮对话历史**每次重发重算** |
| output(completion) | 返回正文 | 通常比 input 贵 3~5 倍 |
| **reasoning(思考)** | 推理模型"想"的过程，**不在 content 里但照样按 output 价计费** | ⚠️ 本项目最大的隐藏成本 |

**必须读 `response.usage`**，尤其 `completion_tokens_details.reasoning_tokens`，别只看 total。

## 二、实测数据（两次调用意外成对照组）

| | 输入质量 | prompt | completion | reasoning(隐形) | 可见文本 | total |
|---|---|---|---|---|---|---|
| Run1 | 乱码/含糊 | 70 | 1019 | **857** | 162 | **1089** |
| Run2 | 清晰中文 | 66 | 54 | **30** | 24 | **120** |

（Run1 的乱码是 `curl -d` 在 Git Bash 下把中文 JSON 传成 mojibake 导致，反而成了天然对照实验。）

### 实测坐实的四件事
1. **思考 token 计费是真的，且是大头**：Run2 一句大白话，completion 54 里 **30 是看不见的 reasoning（56%）**，可见答案只有 24。
2. **输入越含糊，推理模型烧得越狠**（最值钱的发现）：Run1 看不懂输入→reasoning 飙到 857、total 1089；Run2 问题清晰→reasoning 30、total 120。**同模型、几乎同问题，仅"输入清不清楚"就差 9 倍总 token。** 对推理模型，"把问题问清楚"=直接省钱。
3. **小调用有固定开销**：Run2 仅 13 个中文字，prompt 却 66——多出的是 chat 模板/角色标记固定开销。频繁小请求会累积，能合并就合并。
4. **端点通、`qwen3.8-max` 可用**，usage 单列了 `reasoning_tokens`，代码里可精确统计思考成本。

## 三、上下文窗口：input + output 共享一个总上限

- 不是"输入 128K + 输出 128K"，而是**两者之和 ≤ 窗口**。塞得越多，留给输出越小，可能截断。
- 超窗口后果看端点：报错 or **静默截断最早输入**（多轮里"它忘了我开头说的"常是这里）。
- 具体多大：**实测/查中转文档**，别信通用数字（本端点上限尚未实测）。

## 四、什么该塞 / 不该塞（相关密度 > 信息总量）

塞满的四代价：钱↑、延迟↑、**注意力稀释(lost in the middle)**、幻觉↑。加上实测第2条：**含糊/无关输入还会诱发更多 reasoning，双重烧钱。**

**该塞**：当前任务直接相关的代码/文档片段、明确指令+输出schema、少量精准 few-shot、约束与已排除项。
**不该塞**：
- ❌ 整个代码库 → 用 **RAG 按需检索**（阶段1 W6~W10 的核心项目；今天读 `wsaccountmanager` 靠 `sln→csproj→grep` 精准取信号，而非全文喂入）
- ❌ 无关对话历史 → 多轮该**摘要压缩**（W12）
- ❌ 能靠工具现取的数据（库当前值/文件内容）→ function calling（W5）
- ❌ 重复内容、大段样板

## 五、控成本的杠杆

1. 每次记 `usage` 累加成本；推理模型单独盯 `reasoning_tokens`。
2. **把 prompt 写清晰**——对推理模型这是最高性价比的省钱手段（见实测第2条）。
3. 简单任务查端点是否支持 `enable_thinking:false` / `thinking_budget` 关掉或限制深度思考（**本项目未实测，待查端点文档**）。
4. 长文档先切块检索再塞（RAG），别整篇灌。

## 六、C# 侧怎么量（阶段1 预告）

```csharp
var resp = await chatClient.GetResponseAsync<string>(messages);
var u = resp.Usage;   // Microsoft.Extensions.AI
// u.InputTokens / u.OutputTokens（推理模型的 reasoning 是否单列，看 provider 实现）
```
控制：记 usage 累加成本；多轮设 token 预算、超了摘要旧消息；长文档 RAG。

---

**最反直觉**：不是"上下文越大越好、塞满准没错"。恰恰相反——塞得越满越容易被带偏、越贵、越慢，对推理模型还会诱发更多隐形思考。会接 AI 的功力在"只塞相关密度最高的那部分"，这也是 RAG 是阶段1 核心项目而非可选的原因。
