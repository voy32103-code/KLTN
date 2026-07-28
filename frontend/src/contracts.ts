export type LecturerOverrideSelection = {
  matchId: string
  matchType: string
}

export function buildLecturerOverridePayload(
  selections: LecturerOverrideSelection[],
  comment: string,
) {
  return {
    matchOverrides: selections.map(({ matchId, matchType }) => ({
      matchId,
      newMatchType: matchType,
    })),
    comment,
  }
}
