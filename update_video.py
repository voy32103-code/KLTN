import re

with open(r'd:\KLTN\ai-service\app\services\video_processing_service.py', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    'return parse_and_validate_scenario_config(extract_json_string(response.text))',
    'return parse_and_validate_scenario_config(extract_json_string(response.text or ""))'
)

content = content.replace(
    'await asyncio.to_thread(client.files.delete, name=uploaded_file.name)',
    'await asyncio.to_thread(client.files.delete, name=uploaded_file.name)  # type: ignore'
)

with open(r'd:\KLTN\ai-service\app\services\video_processing_service.py', 'w', encoding='utf-8') as f:
    f.write(content)

print('Updated video_processing_service.py')
