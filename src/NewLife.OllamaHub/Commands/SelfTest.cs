using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using NewLife.Log;
using NewLife.OllamaHub.Config;
using NewLife.OllamaHub.Core;
using NewLife.OllamaHub.Diagnostics;
using NewLife.OllamaHub.Security;
using NewLife.Serialization;

namespace NewLife.OllamaHub.Commands
{
    /// <summary>
    /// 内置自检子命令。验证配置加载与协议转换等核心链路，零外部测试框架，
    /// 以退出码表达结果（0=通过，非 0=存在失败项）。
    /// 设计意图：用户在自己的机器上执行 `NewLife.OllamaHub.exe self-test` 即可判断
    /// 是"本地装错了"还是"上游有问题"，无需我们远程排查。
    /// </summary>
    public static class SelfTest
    {
        private static Int32 _pass;
        private static Int32 _fail;

        /// <summary>执行自检并返回进程退出码。</summary>
        /// <returns>0 表示全部通过；非 0 表示失败项数量。</returns>
        public static Int32 Run()
        {
            // 自检是面向用户的诊断命令，结果必须直接打在控制台，
            // 否则用户要去翻 Log 目录才能看到结论，失去"一键排障"的意义
            XTrace.UseConsole();

            _pass = 0;
            _fail = 0;
            XTrace.WriteLine("===== SelfTest 开始 =====");

            CheckConfigLoad();
            CheckRequestConversion();
            CheckStreamPassthrough();
            CheckForceStream();
            CheckDropParams();

            // ---- P0：强制参数覆盖（force-mode）/ 推理缓存（reasoning cache） ----
            CheckForceModeOverride();
            CheckForceModeFillDefault();
            CheckForceModeDropWins();
            CheckReasoningEffortEmission();
            CheckReasoningCacheInjectStore();

            CheckChatResponseConversion();
            CheckGenerateResponseConversion();
            CheckThinkingMapping();
            CheckUpstreamErrorIsBadGateway();

            // ---- M2：流式桥接 / 工具调用 / schema 清洗 ----
            CheckStreamTranslation();
            CheckStreamTranslationGenerate();
            CheckToolSchemaSanitizer();
            CheckToolForwarding();

            // ---- M3：配置 schema / 密钥 / 用量 / 升级逻辑 ----
            CheckSettingsSchema();
            CheckSetKeyRoundTrip();
            CheckUsageStats();
            CheckUpgradeLogic();

            // ---- M4：热重载 / 内置预设 ----
            CheckConfigHotReload();
            CheckProviderPresets();

            // ---- M5：协议转换补强（tool_calls 响应合并） ----
            CheckToolCallsResponseMerge();

            // ---- M6：多模态透传 + Responses/Anthropic/Gemini 上游适配器 ----
            CheckMultimodalOpenAi();
            CheckAdapterFactoryDispatch();
            CheckResponsesRequestConversion();
            CheckResponsesStreamTranslation();
            CheckResponsesNonStream();
            CheckAnthropicStreamTranslation();
            CheckAnthropicNonStream();
            CheckGeminiStreamTranslation();
            CheckGeminiNonStream();

            XTrace.WriteLine("===== SelfTest 完成：通过 {0}，失败 {1} =====", _pass, _fail);
            return _fail;
        }

        // ---- 各项检查 ----

        /// <summary>配置能否加载，且模型均可解析到归属供应商。用合成配置做隔离验证，不依赖部署目录的 settings.json。</summary>
        private static void CheckConfigLoad()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ollamahub_cfgload_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var file = Path.Combine(dir, "settings.json");
                File.WriteAllText(file, "{\"providers\":[{\"id\":\"p1\",\"baseUrl\":\"https://api.x.com/v1\"}],\"models\":[{\"id\":\"m1\",\"provider\":\"p1\"}]}");

                ModelRegistry.Instance.Load(file);
                var models = ModelRegistry.Instance.Models;
                Assert("配置加载：至少注册 1 个模型", models.Count > 0);

                foreach (var m in models)
                {
                    var p = ModelRegistry.Instance.GetProvider(m);
                    Assert($"模型 {m.Id} 能解析到供应商", p != null);
                    if (p != null)
                        Assert($"供应商 {p.Id} 已配置 BaseUrl", !String.IsNullOrEmpty(p.BaseUrl));
                }
            }
            catch (Exception ex)
            {
                Fail("配置加载", ex.Message);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>Ollama 请求 → OpenAI 请求：字段与采样参数是否正确落位。</summary>
        private static void CheckRequestConversion()
        {
            var req = new OllamaChatRequest
            {
                model = "m1",
                stream = false, // M2 起 stream 透传上游；此处显式关流以校验非流式转换
                options = new Dictionary<String, Object> { ["temperature"] = 0.5, ["num_predict"] = 128 },
            };
            req.messages.Add(new OllamaMessage { role = "user", content = "hi" });

            var json = OpenAiAdapter.BuildOpenAiRequest(req, new ModelOptions { Id = "m1" });
            XTrace.WriteLine("  [DEBUG] 上游请求体：{0}", json);

            Assert("请求转换：包含 messages", json.Contains("\"messages\""));
            Assert("请求转换：stream 恒为 false", json.Contains("\"stream\":false"));
            Assert("请求转换：temperature 已透传", json.Contains("\"temperature\":0.5"));
            Assert("请求转换：num_predict → max_tokens", json.Contains("\"max_tokens\":128"));
        }

        /// <summary>dropParams：推理模型不支持的参数必须被剔除，否则上游直接 400。</summary>
        private static void CheckDropParams()
        {
            var req = new OllamaChatRequest
            {
                model = "r1",
                options = new Dictionary<String, Object> { ["temperature"] = 0.7, ["top_p"] = 0.9 },
            };
            req.messages.Add(new OllamaMessage { role = "user", content = "hi" });

            var model = new ModelOptions { Id = "r1", DropParams = new List<String> { "temperature", "top_p" } };
            var json = OpenAiAdapter.BuildOpenAiRequest(req, model);

            Assert("dropParams：temperature 已剔除", !json.Contains("\"temperature\""));
            Assert("dropParams：top_p 已剔除", !json.Contains("\"top_p\""));

            // temperature=0 是合法且常用的取值（确定性输出），绝不能被"省略空值"的序列化策略吞掉
            var zero = new OllamaChatRequest
            {
                model = "m1",
                options = new Dictionary<String, Object> { ["temperature"] = 0.0 },
            };
            zero.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            var zeroJson = OpenAiAdapter.BuildOpenAiRequest(zero, new ModelOptions { Id = "m1" });
            XTrace.WriteLine("  [DEBUG] temperature=0 请求体：{0}", zeroJson);
            Assert("边界：temperature=0 不被吞掉", zeroJson.Contains("\"temperature\":0"));
        }

        /// <summary>P0-3：force-mode 开启时，模型配置的采样值应<b>覆盖</b>客户端下发的值。</summary>
        private static void CheckForceModeOverride()
        {
            var model = new ModelOptions { Id = "fm", OverrideClientParams = true, Temperature = 1.0, TopP = 0.5 };
            var req = new OllamaChatRequest { model = "fm" };
            req.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            req.options = new Dictionary<String, Object> { ["temperature"] = 0.3, ["top_p"] = 0.2 };

            OpenAiAdapter.BuildOpenAiRequest(req, model, forceStream: true);
            Assert("force-mode 覆盖：temperature 被覆写为 1.0", ((Double)req.options["temperature"]) == 1.0);
            Assert("force-mode 覆盖：top_p 被覆写为 0.5", ((Double)req.options["top_p"]) == 0.5);

            var json = OpenAiAdapter.BuildOpenAiRequest(
                new OllamaChatRequest
                {
                    model = "fm",
                    messages = new List<OllamaMessage> { new OllamaMessage { role = "user", content = "hi" } },
                    options = new Dictionary<String, Object> { ["temperature"] = 0.3 },
                }, model, forceStream: true);
            Assert("force-mode 覆盖：上游请求体 temperature=1.0", json.Contains("\"temperature\":1"));
        }

        /// <summary>P0-3：默认模式（不强制）下，仅在客户端缺省时填入模型默认值；已给则不动。</summary>
        private static void CheckForceModeFillDefault()
        {
            var model = new ModelOptions { Id = "fm", Temperature = 1.0 };

            var empty = new OllamaChatRequest { model = "fm" };
            empty.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            OpenAiAdapter.BuildOpenAiRequest(empty, model, forceStream: true);
            Assert("force-mode 默认：客户端缺省→填入 temperature=1.0", ((Double)empty.options["temperature"]) == 1.0);

            var withVal = new OllamaChatRequest { model = "fm" };
            withVal.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            withVal.options = new Dictionary<String, Object> { ["temperature"] = 0.3 };
            OpenAiAdapter.BuildOpenAiRequest(withVal, model, forceStream: true);
            Assert("force-mode 默认：客户端已给→不覆盖(仍 0.3)", ((Double)withVal.options["temperature"]) == 0.3);
        }

        /// <summary>P0-3：dropParams 优先级高于 force-mode——即便强制覆盖，被丢弃的参数也绝不发送。</summary>
        private static void CheckForceModeDropWins()
        {
            var model = new ModelOptions
            {
                Id = "fm",
                OverrideClientParams = true,
                Temperature = 1.0,
                DropParams = new List<String> { "temperature" },
            };
            var req = new OllamaChatRequest { model = "fm" };
            req.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            var json = OpenAiAdapter.BuildOpenAiRequest(req, model, forceStream: true);

            Assert("force-mode：dropParams 优先（temperature 不出现于上游请求）", !json.Contains("\"temperature\""));
            // 内部 opts 仍被赋值（便于审计），但输出已剔除
            Assert("force-mode：drop 后 opts 仍含值（供审计）", req.options.ContainsKey("temperature"));
        }

        /// <summary>P0-3：模型配置 reasoning_effort 应在 openai 上游请求中下发。</summary>
        private static void CheckReasoningEffortEmission()
        {
            var model = new ModelOptions { Id = "r", ReasoningEffort = "high" };
            var req = new OllamaChatRequest { model = "r" };
            req.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            var json = OpenAiAdapter.BuildOpenAiRequest(req, model, forceStream: true);
            Assert("reasoning_effort：模型配置→下发", json.Contains("\"reasoning_effort\":\"high\""));
        }

        /// <summary>P0-1：推理内容多轮缓存——首轮缓存，次轮按前缀指纹重注入助手消息，且客户端自带 thinking 不覆盖、未命中不注入。</summary>
        private static void CheckReasoningCacheInjectStore()
        {
            // 第一轮：用户提问，缓存（真实流程：响应成功后 Store(ComputeKey(全量消息), reasoning)）
            var r1 = new List<OllamaMessage> { new OllamaMessage { role = "user", content = "hi" } };
            var key1 = ReasoningCache.ComputeKey(r1, r1.Count);
            ReasoningCache.Store(key1, "T1");

            // 第二轮：上轮助手回复（无 thinking）进入；注入应回填 T1
            var r2 = new List<OllamaMessage>
            {
                new OllamaMessage { role = "user", content = "hi" },
                new OllamaMessage { role = "assistant", content = "A1" },
                new OllamaMessage { role = "user", content = "follow up" },
            };
            ReasoningCache.Inject(r2);
            Assert("推理缓存：第二轮注入上一轮推理", r2[1].thinking is String s && s == "T1");

            // 已自带 thinking 的助手消息不覆盖（客户端可能已回传）
            var r3 = new List<OllamaMessage>
            {
                new OllamaMessage { role = "user", content = "hi" },
                new OllamaMessage { role = "assistant", content = "A1", thinking = "clientT" },
                new OllamaMessage { role = "user", content = "follow up" },
            };
            ReasoningCache.Inject(r3);
            Assert("推理缓存：已有 thinking 不被覆盖", r3[1].thinking is String s2 && s2 == "clientT");

            // 未命中前缀：无缓存时不注入
            var r4 = new List<OllamaMessage>
            {
                new OllamaMessage { role = "user", content = "unknown" },
                new OllamaMessage { role = "assistant", content = "X" },
            };
            ReasoningCache.Inject(r4);
            Assert("推理缓存：无命中不注入", r4[1].thinking == null || (r4[1].thinking is String e && e.Length == 0));
        }

        /// <summary>OpenAI 响应 → Ollama /api/chat：message 对象形态与用量统计。</summary>
        private static void CheckChatResponseConversion()
        {
            const String oa = """
            {"choices":[{"message":{"role":"assistant","content":"你好"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":3,"completion_tokens":7,"total_tokens":10}}
            """;

            var nd = OpenAiAdapter.ToOllamaNdJson(oa, new ModelOptions { Id = "m1" });

            Assert("chat 响应：内容在 message 对象内", nd.Contains("\"message\"") && nd.Contains("\"你好\""));
            Assert("chat 响应：done=true", nd.Contains("\"done\":true"));
            Assert("chat 响应：eval_count 回填", nd.Contains("\"eval_count\":7"));
            Assert("chat 响应：prompt_eval_count 回填", nd.Contains("\"prompt_eval_count\":3"));
            Assert("chat 响应：NDJSON 以换行结尾", nd.EndsWith("\n"));
            // 真实 Ollama 不输出空字段，多余的 null 会干扰严格解析的客户端
            Assert("chat 响应：不输出 null 字段", !nd.Contains("null"));
        }

        /// <summary>OpenAI 响应 → Ollama /api/generate：必须是 response 字符串而非 message 对象。</summary>
        private static void CheckGenerateResponseConversion()
        {
            const String oa = """
            {"choices":[{"message":{"role":"assistant","content":"世界"},"finish_reason":"stop"}]}
            """;

            var nd = OpenAiAdapter.ToOllamaGenerateNdJson(oa, new ModelOptions { Id = "m1" });

            Assert("generate 响应：使用 response 字段", nd.Contains("\"response\":\"世界\""));
            Assert("generate 响应：不得含 message 对象", !nd.Contains("\"message\""));
        }

        /// <summary>推理模型：上游 reasoning_content 应映射为 Ollama thinking。</summary>
        private static void CheckThinkingMapping()
        {
            const String oa = """
            {"choices":[{"message":{"role":"assistant","content":"答案","reasoning_content":"推理过程"},
             "finish_reason":"stop"}]}
            """;

            var nd = OpenAiAdapter.ToOllamaNdJson(oa, new ModelOptions { Id = "r1" });

            Assert("thinking 映射：reasoning_content → thinking", nd.Contains("\"thinking\":\"推理过程\""));
        }

        /// <summary>上游返回无法解析的内容时，应归类为 502 而非 500。</summary>
        private static void CheckUpstreamErrorIsBadGateway()
        {
            try
            {
                OpenAiAdapter.ToOllamaNdJson("<html>502 Bad Gateway</html>", new ModelOptions { Id = "m1" });
                Fail("上游异常分类", "解析非 JSON 响应本应抛出 HubException");
            }
            catch (HubException ex)
            {
                Assert("上游异常分类：非 JSON 响应归为 502",
                    ex.StatusCode == System.Net.HttpStatusCode.BadGateway);
            }
            catch (Exception ex)
            {
                Fail("上游异常分类", $"期望 HubException，实得 {ex.GetType().Name}");
            }
        }

        /// <summary>M2：stream 字段须按请求原样透传上游（Copilot 默认 stream:true）。</summary>
        private static void CheckStreamPassthrough()
        {
            var offReq = new OllamaChatRequest { model = "m1", stream = false };
            offReq.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            var off = OpenAiAdapter.BuildOpenAiRequest(offReq, new ModelOptions { Id = "m1" });
            Assert("stream 透传：false 下发上游", off.Contains("\"stream\":false"));

            var onReq = new OllamaChatRequest { model = "m1", stream = true };
            onReq.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            var on = OpenAiAdapter.BuildOpenAiRequest(onReq, new ModelOptions { Id = "m1" });
            Assert("stream 透传：true 下发上游", on.Contains("\"stream\":true"));
        }

        /// <summary>M2：forceStream=true 时，无论客户端是否要求流式，都应强制向上游请求 SSE（统一桥接的前提）。</summary>
        private static void CheckForceStream()
        {
            var offReq = new OllamaChatRequest { model = "m1", stream = false };
            offReq.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            var off = OpenAiAdapter.BuildOpenAiRequest(offReq, new ModelOptions { Id = "m1" }, forceStream: true);
            Assert("forceStream：客户端 false 仍强制上游 stream:true", off.Contains("\"stream\":true"));

            var onReq = new OllamaChatRequest { model = "m1", stream = true };
            onReq.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            var on = OpenAiAdapter.BuildOpenAiRequest(onReq, new ModelOptions { Id = "m1" }, forceStream: true);
            Assert("forceStream：客户端 true 仍为 stream:true", on.Contains("\"stream\":true"));
        }

        /// <summary>M2：逐块 SSE → 增量 Ollama 帧（done:false → done:true + usage）。</summary>
        private static void CheckStreamTranslation()
        {
            var tr = new OllamaStreamTranslator(new ModelOptions { Id = "m1" }, forGenerate: false);
            var frames = new List<String>
            {
                tr.Consume("{\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"你\"}}]}"),
                tr.Consume("{\"choices\":[{\"delta\":{\"content\":\"好\"}}]}"),
                tr.Consume("{\"choices\":[{\"delta\":{}}],\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":3}}"),
            };

            Assert("流式翻译：累积内容为完整串", String.Join("\n", frames).Contains("\"content\":\"你好\""));
            Assert("流式翻译：每帧 done=false", frames.TrueForAll(f => f.Contains("\"done\":false")));

            var fin = tr.Finalize();
            Assert("流式翻译：末帧 done=true", fin.Contains("\"done\":true"));
            Assert("流式翻译：末帧含 eval_count", fin.Contains("\"eval_count\":3"));
            Assert("流式翻译：末帧含 prompt_eval_count", fin.Contains("\"prompt_eval_count\":2"));
        }

        /// <summary>M2：/api/generate 的流式帧须用 response 字符串而非 message 对象。</summary>
        private static void CheckStreamTranslationGenerate()
        {
            var tr = new OllamaStreamTranslator(new ModelOptions { Id = "m1" }, forGenerate: true);
            var frames = new List<String>
            {
                tr.Consume("{\"choices\":[{\"delta\":{\"content\":\"世\"}}]}"),
                tr.Consume("{\"choices\":[{\"delta\":{\"content\":\"界\"}}]}"),
            };
            var all = String.Join("\n", frames) + tr.Finalize();
            Assert("generate 流式：使用 response 字段", all.Contains("\"response\":\"世界\""));
            Assert("generate 流式：不含 message 对象", !all.Contains("\"message\""));
        }

        /// <summary>M2：工具 schema 清洗须剔除上游不识别的键，并补 parameters.type=object。</summary>
        private static void CheckToolSchemaSanitizer()
        {
            var tools = new List<Object>
            {
                new Dictionary<String, Object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<String, Object>
                    {
                        ["name"] = "get_weather",
                        ["description"] = "获取天气",
                        ["parameters"] = new Dictionary<String, Object>
                        {
                            ["$schema"] = "http://json-schema.org/draft-07/schema",
                            ["title"] = "Weather",
                            ["additionalProperties"] = false,
                            ["type"] = "object",
                            ["properties"] = new Dictionary<String, Object> { ["city"] = new Dictionary<String, Object> { ["type"] = "string" } },
                            ["x-vendor-ext"] = "drop-me",
                        },
                    },
                },
            };

            var cleaned = ToolSchemaSanitizer.Sanitize(tools);
            var json = JsonHelper.ToJson(cleaned);

            Assert("schema 清洗：剔除 $schema", !json.Contains("$schema"));
            Assert("schema 清洗：剔除 additionalProperties", !json.Contains("additionalProperties"));
            Assert("schema 清洗：剔除 title", !json.Contains("\"title\""));
            Assert("schema 清洗：剔除 x- 扩展键", !json.Contains("x-vendor-ext"));
            Assert("schema 清洗：保留函数名与 parameters", json.Contains("get_weather") && json.Contains("\"parameters\""));
            Assert("schema 清洗：parameters 保留 type=object", json.Contains("\"type\":\"object\""));
        }

        /// <summary>M2：工具须透传上游且经清洗，tool_choice 原样透传。</summary>
        private static void CheckToolForwarding()
        {
            var tools = new List<Object>
            {
                new Dictionary<String, Object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<String, Object>
                    {
                        ["name"] = "f",
                        ["parameters"] = new Dictionary<String, Object> { ["$schema"] = "x" },
                    },
                },
            };

            var req = new OllamaChatRequest { model = "m1", stream = false, tools = tools, tool_choice = "auto" };
            req.messages.Add(new OllamaMessage { role = "user", content = "hi" });
            var json = OpenAiAdapter.BuildOpenAiRequest(req, new ModelOptions { Id = "m1" });

            Assert("工具透传：请求含 tools", json.Contains("\"tools\""));
            Assert("工具透传：含工具名 f", json.Contains("\"f\""));
            Assert("工具透传：清洗掉 $schema", !json.Contains("$schema"));
            Assert("工具透传：tool_choice 透传", json.Contains("\"tool_choice\":\"auto\""));
        }

        /// <summary>M4：settings.json 变更后重载应为"整体替换"而非"累加合并"，否则热重载会残留已删除的模型。</summary>
        private static void CheckConfigHotReload()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ollamahub_hot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var file = Path.Combine(dir, "settings.json");
                File.WriteAllText(file, "{\"providers\":[{\"id\":\"p1\",\"baseUrl\":\"https://x\"}],\"models\":[{\"id\":\"m1\",\"provider\":\"p1\"}]}");
                ModelRegistry.Instance.Load(file);
                Assert("热重载：首次加载 1 个模型", ModelRegistry.Instance.Models.Count == 1 && ModelRegistry.Instance.Models[0].Id == "m1");

                // 模拟 setkey / 编辑器把配置改成只有 m2（m1 被移除）
                File.WriteAllText(file, "{\"providers\":[{\"id\":\"p2\",\"baseUrl\":\"https://y\"}],\"models\":[{\"id\":\"m2\",\"provider\":\"p2\"}]}");
                ModelRegistry.Instance.Load(file);

                Assert("热重载：重载后为整体替换", ModelRegistry.Instance.Models.Count == 1);
                Assert("热重载：旧模型 m1 已被移除", !ModelRegistry.Instance.Models.Any(m => m.Id == "m1"));
                Assert("热重载：新模型 m2 已生效", ModelRegistry.Instance.Models.Any(m => m.Id == "m2"));
            }
            catch (Exception ex)
            {
                Fail("热重载", ex.Message);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>M4：9 家内置供应商预设齐备，且脚手架可序列化为合法 settings.json。</summary>
        private static void CheckProviderPresets()
        {
            Assert("预设：共 11 家内置供应商", ProviderPresets.All.Count == 11);
            foreach (var p in ProviderPresets.All)
            {
                Assert($"预设 {p.Id}：baseUrl 非空", !String.IsNullOrEmpty(p.BaseUrl));
                Assert($"预设 {p.Id}：至少 1 个已知模型", p.Models.Count > 0);
                foreach (var m in p.Models)
                    Assert($"预设 {p.Id} 模型 {m.Id}：已归属供应商", !String.IsNullOrEmpty(m.Provider));
            }

            // 脚手架：序列化 → 反序列化 往返不丢供应商
            var settings = ProviderPresets.BuildSettings(ProviderPresets.All);
            settings.Normalize();
            var json = JsonHelper.ToJson(settings);
            var back = JsonHelper.ToJsonEntity<HubSettings>(json);
            Assert("预设：脚手架含 11 家供应商", back.Providers.Count == 11);
            Assert("预设：脚手架含模型", back.Models.Count > 0);
        }

        /// <summary>M5：流式 tool_calls 跨块合并进 Ollama message.tool_calls（Copilot Agent 模式依赖）。</summary>
        private static void CheckToolCallsResponseMerge()
        {
            var tr = new OllamaStreamTranslator(new ModelOptions { Id = "m1" }, forGenerate: false);
            // 第一块：函数名 + arguments 前半
            tr.Consume("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\\\"\"}}]}}]}");
            // 第二块：arguments 后半（跨块拼接）
            tr.Consume("{\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"Beijing\\\"}\"}}]}}]}");
            var fin = tr.Finalize();

            Assert("tool_calls 响应：合并出 tool_calls", fin.Contains("\"tool_calls\""));
            Assert("tool_calls 响应：含调用 id", fin.Contains("call_1"));
            Assert("tool_calls 响应：含函数名", fin.Contains("get_weather"));
            Assert("tool_calls 响应：arguments 跨块拼接含 Beijing", fin.Contains("Beijing"));
            Assert("tool_calls 响应：末帧 done=true", fin.Contains("\"done\":true"));
        }

        /// <summary>M6：OpenAI 多模态——user 消息带 images 时 content 须变为含 image_url 的数组。</summary>
        private static void CheckMultimodalOpenAi()
        {
            var req = new OllamaChatRequest { model = "m1", stream = false };
            req.messages.Add(new OllamaMessage
            {
                role = "user",
                content = "描述这张图",
                images = new List<String> { "data:image/png;base64,iVBORw0KGgo=" },
            });

            var json = new OpenAiUpstreamAdapter().BuildRequest(req, new ModelOptions { Id = "m1" }, forceStream: false);

            Assert("多模态：请求含 image_url", json.Contains("\"image_url\""));
            Assert("多模态：图片以 data URI 内联", json.Contains("data:image/png;base64,iVBORw0KGgo="));
            Assert("多模态：含原始文本", json.Contains("描述这张图"));

            // 纯 base64（无 data URI 前缀）也须正确判定为 png
            var img2 = OpenAiAdapter.SplitImage("iVBORw0KGgoAAAA");
            Assert("多模态：纯 base64 默认 image/png", img2.mime == "image/png" && img2.b64 == "iVBORw0KGgoAAAA");
        }

        /// <summary>M6：上游适配器工厂按 ApiMode 正确分发，未知模式回落 openai。</summary>
        private static void CheckAdapterFactoryDispatch()
        {
            Assert("适配器工厂：responses → Responses 适配器", UpstreamAdapterFactory.Get("responses") is ResponsesUpstreamAdapter);
            Assert("适配器工厂：anthropic → Anthropic 适配器", UpstreamAdapterFactory.Get("anthropic") is AnthropicUpstreamAdapter);
            Assert("适配器工厂：gemini → Gemini 适配器", UpstreamAdapterFactory.Get("gemini") is GeminiUpstreamAdapter);
            Assert("适配器工厂：google → Gemini 适配器", UpstreamAdapterFactory.Get("google") is GeminiUpstreamAdapter);
            Assert("适配器工厂：openai → OpenAI 适配器", UpstreamAdapterFactory.Get("openai") is OpenAiUpstreamAdapter);
            Assert("适配器工厂：空 → OpenAI 适配器", UpstreamAdapterFactory.Get("") is OpenAiUpstreamAdapter);
            Assert("适配器工厂：未知模式回落 openai", UpstreamAdapterFactory.Get("bogus-vendor") is OpenAiUpstreamAdapter);
        }

        /// <summary>M6：Responses 请求应正确转换 input、图片、工具、工具结果与输出 token 参数。</summary>
        private static void CheckResponsesRequestConversion()
        {
            var req = new OllamaChatRequest
            {
                model = "m1",
                stream = false,
                options = new Dictionary<String, Object> { ["num_predict"] = 128, ["temperature"] = 0.3 },
                tools = new List<Object>
                {
                    new Dictionary<String, Object?>
                    {
                        ["type"] = "function",
                        ["function"] = new Dictionary<String, Object?>
                        {
                            ["name"] = "get_weather",
                            ["description"] = "查询天气",
                            ["parameters"] = new Dictionary<String, Object?> { ["type"] = "object" },
                        },
                    },
                },
            };
            req.messages.Add(new OllamaMessage
            {
                role = "user",
                content = "看图",
                images = new List<String> { "data:image/png;base64,AAAA" },
            });
            req.messages.Add(new OllamaMessage
            {
                role = "assistant",
                tool_calls = new List<Object>
                {
                    new Dictionary<String, Object?>
                    {
                        ["id"] = "call_1",
                        ["type"] = "function",
                        ["function"] = new Dictionary<String, Object?> { ["name"] = "get_weather", ["arguments"] = "{\"city\":\"广州\"}" },
                    },
                },
            });
            req.messages.Add(new OllamaMessage { role = "tool", tool_call_id = "call_1", content = "晴" });

            var adapter = new ResponsesUpstreamAdapter();
            var json = adapter.BuildRequest(req, new ModelOptions { Id = "m1", ReasoningEffort = "medium" }, forceStream: true);

            Assert("Responses 请求：URL 拼接 /responses", adapter.GetRequestUrl(new ProviderOptions { BaseUrl = "https://api.openai.com/v1" }, new ModelOptions()) == "https://api.openai.com/v1/responses");
            Assert("Responses 请求：使用 input items", json.Contains("\"input\""));
            Assert("Responses 请求：图片映射 input_image", json.Contains("\"type\":\"input_image\""));
            Assert("Responses 请求：历史工具调用映射 function_call", json.Contains("\"type\":\"function_call\""));
            Assert("Responses 请求：工具结果映射 function_call_output", json.Contains("\"type\":\"function_call_output\""));
            Assert("Responses 请求：max_tokens 映射 max_output_tokens", json.Contains("\"max_output_tokens\":128"));
            Assert("Responses 请求：工具定义为扁平 name", json.Contains("\"name\":\"get_weather\""));
            Assert("Responses 请求：reasoning effort 已下发", json.Contains("\"reasoning\":{\"effort\":\"medium\"}"));
            Assert("Responses 请求：强制上游流式", json.Contains("\"stream\":true"));
        }

        /// <summary>M6：Responses SSE 应正确翻译文本、推理、工具调用、结束原因和 token 用量。</summary>
        private static void CheckResponsesStreamTranslation()
        {
            var events = new List<Object>
            {
                new Dictionary<String, Object?> { ["type"] = "response.output_text.delta", ["delta"] = "Hello" },
                new Dictionary<String, Object?> { ["type"] = "response.reasoning_summary_text.delta", ["delta"] = "think" },
                new Dictionary<String, Object?>
                {
                    ["type"] = "response.output_item.added",
                    ["output_index"] = 1,
                    ["item"] = new Dictionary<String, Object?>
                    {
                        ["type"] = "function_call", ["id"] = "fc_1", ["call_id"] = "call_1",
                        ["name"] = "get_weather", ["arguments"] = "",
                    },
                },
                new Dictionary<String, Object?>
                {
                    ["type"] = "response.function_call_arguments.delta", ["item_id"] = "fc_1",
                    ["output_index"] = 1, ["delta"] = "{\"city\":\"广州\"}",
                },
                new Dictionary<String, Object?>
                {
                    ["type"] = "response.completed",
                    ["response"] = new Dictionary<String, Object?>
                    {
                        ["status"] = "completed",
                        ["usage"] = new Dictionary<String, Object?> { ["input_tokens"] = 5, ["output_tokens"] = 9 },
                    },
                },
            };
            var sse = new StringBuilder();
            foreach (var item in events)
            {
                var type = (item as Dictionary<String, Object?>)?.Val("type")?.ToString() ?? "";
                sse.Append("event: ").Append(type).Append('\n');
                sse.Append("data: ").Append(JsonHelper.ToJson(item)).Append("\n\n");
            }

            using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(sse.ToString()) };
            var chunks = new List<String>();
            new ResponsesUpstreamAdapter().ReadStream(response, chunks.Add, CancellationToken.None);
            var translator = new OllamaStreamTranslator(new ModelOptions { Id = "m1" }, forGenerate: false);
            var ollama = String.Join("\n", chunks.Select(translator.Consume)) + translator.Finalize(includeContent: false);

            Assert("Responses 流：文本 Hello 已翻译", ollama.Contains("\"content\":\"Hello\""));
            Assert("Responses 流：推理摘要映射 thinking", ollama.Contains("\"thinking\":\"think\""));
            Assert("Responses 流：工具调用 id 透传", ollama.Contains("call_1"));
            Assert("Responses 流：工具名透传", ollama.Contains("get_weather"));
            Assert("Responses 流：工具参数增量已合并", ollama.Contains("广州"));
            Assert("Responses 流：工具调用结束原因", ollama.Contains("\"done_reason\":\"stop\"") && ollama.Contains("\"tool_calls\""));
            Assert("Responses 流：eval_count 回填", ollama.Contains("\"eval_count\":9"));
            Assert("Responses 流：prompt_eval_count 回填", ollama.Contains("\"prompt_eval_count\":5"));
        }

        /// <summary>M6：Responses 非流式 output items 应转换为统一 OpenAI 形状并可继续生成 Ollama 帧。</summary>
        private static void CheckResponsesNonStream()
        {
            var json = JsonHelper.ToJson(new Dictionary<String, Object?>
            {
                ["id"] = "resp_1",
                ["status"] = "completed",
                ["model"] = "m1",
                ["output"] = new List<Object?>
                {
                    new Dictionary<String, Object?>
                    {
                        ["type"] = "reasoning",
                        ["summary"] = new List<Object?> { new Dictionary<String, Object?> { ["type"] = "summary_text", ["text"] = "分析" } },
                    },
                    new Dictionary<String, Object?>
                    {
                        ["type"] = "message",
                        ["content"] = new List<Object?> { new Dictionary<String, Object?> { ["type"] = "output_text", ["text"] = "答案" } },
                    },
                    new Dictionary<String, Object?>
                    {
                        ["type"] = "function_call", ["id"] = "fc_1", ["call_id"] = "call_1",
                        ["name"] = "lookup", ["arguments"] = "{\"id\":1}",
                    },
                },
                ["usage"] = new Dictionary<String, Object?> { ["input_tokens"] = 3, ["output_tokens"] = 7 },
            });

            var oaLike = new ResponsesUpstreamAdapter().ConvertNonStream(json, new ModelOptions { Id = "m1" });
            var ollama = OpenAiAdapter.ToOllamaNdJson(oaLike, new ModelOptions { Id = "m1" });

            Assert("Responses 非流式：输出文本已转换", ollama.Contains("\"content\":\"答案\""));
            Assert("Responses 非流式：推理摘要已转换", ollama.Contains("\"thinking\":\"分析\""));
            Assert("Responses 非流式：工具调用已转换", ollama.Contains("lookup") && ollama.Contains("call_1"));
            Assert("Responses 非流式：eval_count 回填", ollama.Contains("\"eval_count\":7"));
            Assert("Responses 非流式：prompt_eval_count 回填", ollama.Contains("\"prompt_eval_count\":3"));
        }

        /// <summary>M6：Anthropic SSE（event/data 块）经适配器翻译后，Ollama 帧含文本 / 工具调用 / 用量。</summary>
        private static void CheckAnthropicStreamTranslation()
        {
            var sse = new StringBuilder();
            void Ev(String type, Object data)
            {
                sse.Append("event: ").Append(type).Append('\n');
                sse.Append("data: ").Append(JsonHelper.ToJson(data)).Append("\n\n");
            }

            Ev("message_start", new Dictionary<String, Object?> { ["type"] = "message_start", ["message"] = new Dictionary<String, Object?> { ["usage"] = new Dictionary<String, Object?> { ["input_tokens"] = 5 } } });
            Ev("content_block_start", new Dictionary<String, Object?> { ["type"] = "content_block_start", ["index"] = 0, ["content_block"] = new Dictionary<String, Object?> { ["type"] = "text", ["text"] = "" } });
            Ev("content_block_delta", new Dictionary<String, Object?> { ["type"] = "content_block_delta", ["index"] = 0, ["delta"] = new Dictionary<String, Object?> { ["type"] = "text_delta", ["text"] = "Hello" } });
            Ev("content_block_start", new Dictionary<String, Object?> { ["type"] = "content_block_start", ["index"] = 1, ["content_block"] = new Dictionary<String, Object?> { ["type"] = "tool_use", ["id"] = "tu_1", ["name"] = "get_weather" } });
            Ev("content_block_delta", new Dictionary<String, Object?> { ["type"] = "content_block_delta", ["index"] = 1, ["delta"] = new Dictionary<String, Object?> { ["type"] = "input_json_delta", ["partial_json"] = "{\"city\":" } });
            Ev("content_block_delta", new Dictionary<String, Object?> { ["type"] = "content_block_delta", ["index"] = 1, ["delta"] = new Dictionary<String, Object?> { ["type"] = "input_json_delta", ["partial_json"] = "\"Beijing\"}" } });
            Ev("message_delta", new Dictionary<String, Object?> { ["type"] = "message_delta", ["delta"] = new Dictionary<String, Object?> { ["stop_reason"] = "tool_use" }, ["usage"] = new Dictionary<String, Object?> { ["output_tokens"] = 12 } });
            Ev("message_stop", new Dictionary<String, Object?> { ["type"] = "message_stop" });

            using var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(sse.ToString()) };
            var frames = new List<String>();
            new AnthropicUpstreamAdapter().ReadStream(resp, frames.Add, CancellationToken.None);

            var tr = new OllamaStreamTranslator(new ModelOptions { Id = "m1" }, forGenerate: false);
            var ollama = String.Join("\n", frames.Select(f => tr.Consume(f))) + tr.Finalize(includeContent: false);

            Assert("Anthropic 流：文本 Hello 已翻译", ollama.Contains("\"content\":\"Hello\""));
            Assert("Anthropic 流：工具调用 id 透传", ollama.Contains("tu_1"));
            Assert("Anthropic 流：工具名透传", ollama.Contains("get_weather"));
            Assert("Anthropic 流：arguments 跨块拼接含 Beijing", ollama.Contains("Beijing"));
            Assert("Anthropic 流：末帧 done=true", ollama.Contains("\"done\":true"));
            Assert("Anthropic 流：eval_count 回填", ollama.Contains("\"eval_count\":12"));
            Assert("Anthropic 流：prompt_eval_count 回填", ollama.Contains("\"prompt_eval_count\":5"));
        }

        /// <summary>M6：Anthropic 非流式响应（content 数组含 text + tool_use）转换为 Ollama 帧。</summary>
        private static void CheckAnthropicNonStream()
        {
            var json = JsonHelper.ToJson(new Dictionary<String, Object?>
            {
                ["content"] = new List<Object?>
                {
                    new Dictionary<String, Object?> { ["type"] = "text", ["text"] = "Hello" },
                    new Dictionary<String, Object?> { ["type"] = "tool_use", ["id"] = "t1", ["name"] = "f", ["input"] = new Dictionary<String, Object?> { ["a"] = 1 } },
                },
                ["stop_reason"] = "end_turn",
                ["usage"] = new Dictionary<String, Object?> { ["input_tokens"] = 2, ["output_tokens"] = 4 },
            });

            var oaLike = new AnthropicUpstreamAdapter().ConvertNonStream(json, new ModelOptions { Id = "m1" });
            var nd = OpenAiAdapter.ToOllamaNdJson(oaLike, new ModelOptions { Id = "m1" });

            Assert("Anthropic 非流式：文本 Hello", nd.Contains("\"content\":\"Hello\""));
            Assert("Anthropic 非流式：工具名 f", nd.Contains("\"f\""));
            Assert("Anthropic 非流式：eval_count 回填", nd.Contains("\"eval_count\":4"));
        }

        /// <summary>M6：Gemini SSE（data 行）经适配器翻译后，Ollama 帧含文本 / 用量 / 思考。</summary>
        private static void CheckGeminiStreamTranslation()
        {
            // 1) 普通文本 + 用量
            var sseText = "data: " + JsonHelper.ToJson(new Dictionary<String, Object?>
            {
                ["candidates"] = new List<Object?>
                {
                    new Dictionary<String, Object?>
                    {
                        ["content"] = new Dictionary<String, Object?> { ["parts"] = new List<Object?> { new Dictionary<String, Object?> { ["text"] = "Hi" } } },
                        ["finishReason"] = "STOP",
                    },
                },
                ["usageMetadata"] = new Dictionary<String, Object?> { ["promptTokenCount"] = 3, ["candidatesTokenCount"] = 7 },
            }) + "\n";

            using var respText = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(sseText) };
            var frames = new List<String>();
            new GeminiUpstreamAdapter().ReadStream(respText, frames.Add, CancellationToken.None);
            var tr = new OllamaStreamTranslator(new ModelOptions { Id = "m1" }, forGenerate: false);
            var ollamaText = String.Join("\n", frames.Select(f => tr.Consume(f))) + tr.Finalize();
            Assert("Gemini 流：文本 Hi 已翻译", ollamaText.Contains("\"content\":\"Hi\""));
            Assert("Gemini 流：eval_count 回填", ollamaText.Contains("\"eval_count\":7"));
            Assert("Gemini 流：prompt_eval_count 回填", ollamaText.Contains("\"prompt_eval_count\":3"));

            // 2) 思考过程（thought:true 的 part）映射为 thinking
            var sseThink = "data: " + JsonHelper.ToJson(new Dictionary<String, Object?>
            {
                ["candidates"] = new List<Object?>
                {
                    new Dictionary<String, Object?>
                    {
                        ["content"] = new Dictionary<String, Object?>
                        {
                            ["parts"] = new List<Object?>
                            {
                                new Dictionary<String, Object?> { ["thought"] = true, ["text"] = "hmm" },
                                new Dictionary<String, Object?> { ["text"] = "ans" },
                            },
                        },
                    },
                },
            }) + "\n";

            using var respThink = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(sseThink) };
            var frames2 = new List<String>();
            new GeminiUpstreamAdapter().ReadStream(respThink, frames2.Add, CancellationToken.None);
            var tr2 = new OllamaStreamTranslator(new ModelOptions { Id = "m1" }, forGenerate: false);
            var ollamaThink = String.Join("\n", frames2.Select(f => tr2.Consume(f))) + tr2.Finalize(includeContent: false);
            Assert("Gemini 流：思考过程映射为 thinking", ollamaThink.Contains("\"thinking\":\"hmm\""));
            Assert("Gemini 流：正文 ans", ollamaThink.Contains("\"content\":\"ans\""));
        }

        /// <summary>M6：Gemini 非流式响应转换为 Ollama 帧。</summary>
        private static void CheckGeminiNonStream()
        {
            var json = JsonHelper.ToJson(new Dictionary<String, Object?>
            {
                ["candidates"] = new List<Object?>
                {
                    new Dictionary<String, Object?>
                    {
                        ["content"] = new Dictionary<String, Object?> { ["parts"] = new List<Object?> { new Dictionary<String, Object?> { ["text"] = "World" } } },
                    },
                },
                ["usageMetadata"] = new Dictionary<String, Object?> { ["promptTokenCount"] = 1, ["candidatesTokenCount"] = 2 },
            });

            var oaLike = new GeminiUpstreamAdapter().ConvertNonStream(json, new ModelOptions { Id = "m1" });
            var nd = OpenAiAdapter.ToOllamaNdJson(oaLike, new ModelOptions { Id = "m1" });
            Assert("Gemini 非流式：文本 World", nd.Contains("\"content\":\"World\""));
            Assert("Gemini 非流式：eval_count 回填", nd.Contains("\"eval_count\":2"));
        }

        // ---- 断言工具 ----

        /// <summary>M3：HubSettings 兼容 host/port 写法（推导 Url）且 Save/Load 往返保留供应商。</summary>
        private static void CheckSettingsSchema()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ollamahub_cfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var file = Path.Combine(dir, "settings.json");
                File.WriteAllText(file, "{\"host\":\"127.0.0.1\",\"port\":12345,\"providers\":[{\"id\":\"p1\",\"baseUrl\":\"https://x\"}],\"models\":[{\"id\":\"m1\",\"provider\":\"p1\"}]}");
                var s = HubSettings.Load(file);
                Assert("配置 schema：host/port 推导出 Url", s.Url == "http://127.0.0.1:12345");

                // 往返：写入后再读回，供应商/模型不丢
                s.Providers.Add(new ProviderOptions { Id = "p2", BaseUrl = "https://y" });
                s.Models.Add(new ModelOptions { Id = "m2", Provider = "p2" });
                s.Save(file);
                var s2 = HubSettings.Load(file);
                Assert("配置 schema：Save/Load 保留供应商", s2.Providers.Any(p => p.Id == "p2"));
                Assert("配置 schema：Save/Load 保留模型", s2.Models.Any(m => m.Id == "m2"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>M3：SecretProtector 加密→解密还原，且不暴露明文。</summary>
        private static void CheckSetKeyRoundTrip()
        {
            var plain = "sk-secret-abc-123";
            var enc = SecretProtector.Protect(plain);
            Assert("密钥加密：返回 dpapi: 前缀", enc.StartsWith("dpapi:"));
            Assert("密钥加密：与明文不同", enc != plain);
            var dec = SecretProtector.Resolve(new ProviderOptions { ProtectedApiKey = enc });
            Assert("密钥解密：还原明文", dec == plain);
            // env: 形式透传环境变量
            Environment.SetEnvironmentVariable("NHUB_SELFTEST_KEY", plain);
            var fromEnv = SecretProtector.Resolve(new ProviderOptions { ProtectedApiKey = "env:NHUB_SELFTEST_KEY" });
            Assert("密钥解析：env: 形式读出环境变量", fromEnv == plain);
            Environment.SetEnvironmentVariable("NHUB_SELFTEST_KEY", null);
        }

        /// <summary>M3：用量统计累加与快照正确。</summary>
        private static void CheckUsageStats()
        {
            var u = new UsageStats();
            u.RecordSuccess("m1", 3, 7);
            u.RecordSuccess("m1", 1, 2);
            u.RecordError("m2", "boom");
            var snap = u.Snapshot();

            Assert("用量统计：m1 请求数=2", snap["m1"].Requests == 2);
            Assert("用量统计：m1 prompt token=4", snap["m1"].PromptTokens == 4);
            Assert("用量统计：m1 completion token=9", snap["m1"].CompletionTokens == 9);
            Assert("用量统计：m2 错误数=1", snap["m2"].Errors == 1);
            Assert("用量统计：m2 记录末次错误", snap["m2"].LastError == "boom");
        }

        /// <summary>M3：升级纯逻辑——版本比较、清单解析、替换脚本、文件替换。</summary>
        private static void CheckUpgradeLogic()
        {
            Assert("升级：1.2.3 > 1.2.0", UpgradeCommand.CompareVersions("1.2.3", "1.2.0") > 0);
            Assert("升级：1.2.0 < 1.2.3", UpgradeCommand.CompareVersions("1.2.0", "1.2.3") < 0);
            Assert("升级：1.2.3 == 1.2.3", UpgradeCommand.CompareVersions("1.2.3", "1.2.3") == 0);
            Assert("升级：v2.0.0 > 1.9.0（去 v 前缀）", UpgradeCommand.CompareVersions("v2.0.0", "1.9.0") > 0);

            var (ver, url, notes) = UpgradeCommand.ParseManifest("{\"version\":\"1.5.0\",\"url\":\"http://x/a.exe\",\"notes\":\"fix\"}");
            Assert("升级：清单解析 version", ver == "1.5.0");
            Assert("升级：清单解析 url", url == "http://x/a.exe");

            var gh = UpgradeCommand.ParseManifest("{\"tag_name\":\"v2.0.0\",\"assets\":[{\"browser_download_url\":\"http://x/b.exe\"}]}");
            Assert("升级：GitHub 清单取 tag_name", gh.version == "2.0.0");
            Assert("升级：GitHub 清单取 asset url", gh.url == "http://x/b.exe");

            var script = UpgradeCommand.BuildReplaceScript(1234, @"C:\svc\a.exe", @"C:\tmp\b.exe", "NewLifeOllamaHub");
            Assert("升级脚本：等待指定 PID", script.Contains("PID eq 1234"));
            Assert("升级脚本：执行 move /Y", script.Contains("move /Y"));
            Assert("升级脚本：重启服务 net start", script.Contains("net start NewLifeOllamaHub"));

            var dir = Path.Combine(Path.GetTempPath(), "ollamahub_upg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var cur = Path.Combine(dir, "cur.exe");
                var dl = Path.Combine(dir, "dl.exe");
                File.WriteAllText(cur, "old");
                File.WriteAllText(dl, "new");
                UpgradeCommand.PerformReplace(cur, dl);
                Assert("升级：PerformReplace 替换文件内容", File.Exists(cur) && File.ReadAllText(cur) == "new" && !File.Exists(dl));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void Assert(String name, Boolean ok)        {
            if (ok)
            {
                _pass++;
                XTrace.WriteLine("  [PASS] {0}", name);
            }
            else
            {
                _fail++;
                XTrace.WriteLine("  [FAIL] {0}", name);
            }
        }

        private static void Fail(String name, String reason)
        {
            _fail++;
            XTrace.WriteLine("  [FAIL] {0}：{1}", name, reason);
        }
    }
}
