import re

with open(r'd:\KLTN\ai-service\tests\test_debug_regressions.py', 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    'from unittest.mock import AsyncMock, patch',
    'from unittest.mock import AsyncMock, patch, MagicMock'
)

with open(r'd:\KLTN\ai-service\tests\test_debug_regressions.py', 'w', encoding='utf-8') as f:
    f.write(content)

print('Updated test_debug_regressions.py')
