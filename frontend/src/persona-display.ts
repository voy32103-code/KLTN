const PERSONA_TEXT_TRANSLATIONS: ReadonlyArray<readonly [string, string]> = [
  ['Business Owner', 'Chủ sở hữu nghiệp vụ'],
  ['Process Expert', 'Chuyên gia quy trình'],
  ['End User', 'Người dùng cuối'],
  ['Decision Maker', 'Người ra quyết định'],
  ['Domain Specialist', 'Chuyên gia nghiệp vụ'],
  ['Operational User', 'Người dùng vận hành'],
  ['Management', 'Quản lý'],
  ['Operations', 'Vận hành'],
  ['Delivery', 'Triển khai'],
  ['Collaborative', 'Hợp tác'],
  ['Challenging', 'Phản biện'],
  ['collaborative', 'Hợp tác'],
  ['concise', 'Ngắn gọn'],
  ['detail_oriented', 'Chú trọng chi tiết'],
  ['Easy', 'Dễ'],
  ['Medium', 'Trung bình'],
  ['Hard', 'Khó'],
  ['easy', 'Dễ'],
  ['medium', 'Trung bình'],
  ['hard', 'Khó'],
  ['high', 'Cao'],
  ['low', 'Thấp'],
  ['neutral', 'Trung lập'],
]

/**
 * Keeps stored identifiers and custom admin wording unchanged while presenting
 * known system persona metadata in Vietnamese to students and lecturers.
 */
export function formatPersonaText(value?: string | null): string {
  if (!value) return ''

  return PERSONA_TEXT_TRANSLATIONS.reduce(
    (formatted, [source, translated]) => formatted.split(source).join(translated),
    value,
  )
}
