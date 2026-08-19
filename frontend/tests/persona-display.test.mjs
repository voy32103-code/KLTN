import assert from 'node:assert/strict';
import test from 'node:test';

import { formatPersonaText } from '../src/persona-display.ts';

test('persona metadata is presented in clear Vietnamese', () => {
  assert.equal(
    formatPersonaText('Business Owner - Collaborative'),
    'Chủ sở hữu nghiệp vụ - Hợp tác',
  );
  assert.equal(formatPersonaText('concise'), 'Ngắn gọn');
  assert.equal(formatPersonaText('high'), 'Cao');
  assert.equal(formatPersonaText('Custom persona'), 'Custom persona');
});
