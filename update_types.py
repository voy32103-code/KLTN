import re

with open(r'd:\KLTN\ai-service\app\services\api_client_manager.py', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix api_key type
content = content.replace(
    'api_key: str,',
    'api_key: str | None,'
)

# Fix system_instruction type
content = content.replace(
    'system_instruction: str | None,',
    'system_instruction: Any,'
)

with open(r'd:\KLTN\ai-service\app\services\api_client_manager.py', 'w', encoding='utf-8') as f:
    f.write(content)

print('Updated api_client_manager.py')
