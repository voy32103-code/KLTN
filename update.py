import re

models_list = [
    ('omniroute/kmc/k3', 'Kimi K3', 'Kimi K3'),
    ('omniroute/kmc/kimi-for-coding', 'Kimi for Coding', 'Kimi for Coding'),
    ('omniroute/kmc/kimi-for-coding-highspeed', 'Kimi Coding Fast', 'Kimi for Coding Highspeed'),
    ('omniroute/cp/cline-pass/glm-5.2', 'GLM-5.2', 'GLM-5.2'),
    ('omniroute/cp/cline-pass/minimax-m3', 'MiniMax-M3', 'MiniMax-M3'),
    ('omniroute/cp/cline-pass/deepseek-v4-pro', 'DeepSeek V4 Pro', 'DeepSeek V4 Pro'),
    ('omniroute/cp/cline-pass/deepseek-v4-flash', 'DeepSeek V4 Flash', 'DeepSeek V4 Flash'),
    ('omniroute/cp/cline-pass/kimi-k3', 'Kimi K3 CP', 'Kimi K3'),
    ('omniroute/cp/cline-pass/kimi-k2.7-code', 'Kimi K2.7 Code', 'Kimi K2.7 Code'),
    ('omniroute/cp/cline-pass/mimo-v2.5-pro', 'MiMo-V2.5-Pro', 'MiMo-V2.5-Pro'),
    ('omniroute/cp/cline-pass/mimo-v2.5', 'MiMo-V2.5', 'MiMo-V2.5'),
    ('omniroute/cp/cline-pass/qwen3.7-max', 'Qwen3.7 Max', 'Qwen3.7 Max'),
    ('omniroute/cp/cline-pass/qwen3.7-plus', 'Qwen3.7 Plus', 'Qwen3.7 Plus'),
    ('omniroute/kr/claude-sonnet-5', 'Claude Sonnet 5', 'Claude Sonnet 5'),
    ('omniroute/kr/claude-sonnet-4.5', 'Claude Sonnet 4.5', 'Claude Sonnet 4.5'),
    ('omniroute/kr/claude-haiku-4.5', 'Claude Haiku 4.5', 'Claude Haiku 4.5'),
    ('omniroute/kr/deepseek-3.2', 'DeepSeek V3.2', 'DeepSeek V3.2'),
    ('omniroute/kr/minimax-m2.5', 'MiniMax M2.5', 'MiniMax M2.5'),
    ('omniroute/kr/minimax-m2.1', 'MiniMax M2.1', 'MiniMax M2.1'),
    ('omniroute/kr/glm-5', 'GLM-5', 'GLM-5'),
    ('omniroute/kr/qwen3-coder-next', 'Qwen3 Coder Next', 'Qwen3 Coder Next'),
    ('omniroute/kr/gpt-5.6-sol', 'GPT-5.6 Sol', 'GPT-5.6 Sol'),
    ('omniroute/kr/gpt-5.6-terra', 'GPT-5.6 Terra', 'GPT-5.6 Terra'),
    ('omniroute/kr/gpt-5.6-luna', 'GPT-5.6 Luna', 'GPT-5.6 Luna')
]

with open(r'd:\KLTN\frontend\src\views.ts', 'r', encoding='utf-8') as f:
    views_ts = f.read().replace('\r\n', '\n')

# 1. Update getFriendlyModelName
friendly_cases = [f"    case '{m[0]}': return '{m[1]} (OmniRoute)'" for m in models_list]
friendly_str = '\n'.join(friendly_cases) + '\n    default: return modelId'
views_ts = re.sub(r'    case \'omniroute/google/gemini-2.5-flash\': return \'Gemini 2.5 Flash \(OmniRoute\)\'\n    default: return modelId', friendly_str, views_ts)
views_ts = views_ts.replace("    case 'omniroute/meta-llama/llama-3.3-70b-instruct': return 'Llama 3.3 70B (OmniRoute)'\n", "")
views_ts = views_ts.replace("    case 'omniroute/deepseek/deepseek-chat': return 'DeepSeek Chat (OmniRoute)'\n", "")

# 2. Update renderModelGroups
group_items = [f"    {{ id: '{m[0]}', name: '{m[1]} (Omni)', provider: 'omniroute', desc: '{m[2]} qua OmniRoute' }}" for m in models_list]
group_str = ',\n'.join(group_items) + '\n  ];'
views_ts = re.sub(r"    { id: 'omniroute/google/gemini-2.5-flash', name: 'Gemini 2.5 Flash \(Omni\)', provider: 'omniroute', desc: 'Gemini 2.5 qua OmniRoute' }\n  ];", group_str, views_ts)
views_ts = views_ts.replace("    { id: 'omniroute/meta-llama/llama-3.3-70b-instruct', name: 'Llama 3.3 70B (Omni)', provider: 'omniroute', desc: 'Llama 3.3 qua OmniRoute' },\n", "")
views_ts = views_ts.replace("    { id: 'omniroute/deepseek/deepseek-chat', name: 'DeepSeek Chat (Omni)', provider: 'omniroute', desc: 'DeepSeek qua OmniRoute' },\n", "")

# 3. Update admin-crawl-model-select
opt_items = [f'            <option value="{m[0]}">{m[1]}</option>' for m in models_list]
opt_str = '\n'.join(opt_items) + '\n          </select>'
views_ts = re.sub(r'            <option value="omniroute/google/gemini-2.5-flash">Gemini 2.5 Flash \(OmniRoute\)</option>\n          </select>', opt_str, views_ts)
views_ts = views_ts.replace('            <option value="omniroute/meta-llama/llama-3.3-70b-instruct">Llama 3.3 70B (OmniRoute)</option>\n', "")
views_ts = views_ts.replace('            <option value="omniroute/deepseek/deepseek-chat">DeepSeek Chat (OmniRoute)</option>\n', "")

with open(r'd:\KLTN\frontend\src\views.ts', 'w', encoding='utf-8') as f:
    f.write(views_ts)

with open(r'd:\KLTN\backend\ReqSimulator.API\Services\AiModelCatalog.cs', 'r', encoding='utf-8') as f:
    catalog = f.read().replace('\r\n', '\n')

catalog_items = [f'        "{m[0]}"' for m in models_list]
catalog_str = ',\n'.join(catalog_items) + '\n    };'
catalog = re.sub(r'        "omniroute/google/gemini-2.5-flash"\n    };', catalog_str, catalog)
catalog = catalog.replace('        "omniroute/meta-llama/llama-3.3-70b-instruct",\n', "")
catalog = catalog.replace('        "omniroute/deepseek/deepseek-chat",\n', "")

with open(r'd:\KLTN\backend\ReqSimulator.API\Services\AiModelCatalog.cs', 'w', encoding='utf-8') as f:
    f.write(catalog)

print('Updated files properly')
