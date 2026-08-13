with open(r'd:\KLTN\ai-service\app\services\api_client_manager.py', 'r', encoding='utf-8') as f:
    lines = f.readlines()

new_lines = []
i = 0
while i < len(lines):
    if '# N?u là OpenRouter -> G?i OpenRouter' in lines[i]:
        # Capture OpenRouter block
        openrouter_block = lines[i:i+13]
        i += 13
        # Ensure we're at the OmniRoute block
        if '# N?u là OmniRoute -> G?i qua OmniRoute Gateway' in lines[i]:
            omniroute_block = lines[i:i+16]
            i += 16
            
            # Swap them
            new_lines.extend(omniroute_block)
            # add an empty line if needed
            new_lines.extend(openrouter_block)
            continue
    new_lines.append(lines[i])
    i += 1

with open(r'd:\KLTN\ai-service\app\services\api_client_manager.py', 'w', encoding='utf-8') as f:
    f.writelines(new_lines)
print("Swapped blocks.")
