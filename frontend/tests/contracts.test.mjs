import assert from 'node:assert/strict';
import test from 'node:test';

import { buildLecturerOverridePayload } from '../src/contracts.ts';

test('lecturer override payload matches the backend DTO contract', () => {
  const payload = buildLecturerOverridePayload(
    [{ matchId: '11111111-1111-1111-1111-111111111111', matchType: 'exact' }],
    'reviewed',
  );

  assert.deepEqual(payload, {
    matchOverrides: [
      {
        matchId: '11111111-1111-1111-1111-111111111111',
        newMatchType: 'exact',
      },
    ],
    comment: 'reviewed',
  });
});
