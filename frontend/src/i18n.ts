export const i18n = {
  roles: {
    'Student': 'Sinh viên',
    'Admin': 'Quản trị viên',
    'Lecturer': 'Giảng viên'
  },
  status: {
    'AwaitingUpload': 'Chờ tải lên',
    'Queued': 'Đang chờ',
    'Processing': 'Đang xử lý',
    'AwaitingReview': 'Chờ duyệt',
    'Published': 'Đã publish',
    'Failed': 'Thất bại'
  },
  moods: {
    'neutral': 'Bình thường',
    'neutral_busy': 'Bận rộn',
    'rushed': 'Vội vã',
    'cooperative': 'Hợp tác'
  },
  difficulty: {
    'Easy': 'Dễ',
    'Medium': 'Trung bình',
    'Hard': 'Khó'
  }
}

export function t(key: string | undefined | null, dict: Record<string, string>, fallback: string): string {
  if (!key) return fallback;
  return dict[key] || fallback;
}
