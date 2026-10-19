// The four pilot reason labels for a report or incident. They are not a formal railway
// classification, so an unknown code is shown as it came rather than guessed at.

export const reasonNames: Record<string, string> = {
  TRAIN_FAULT: 'Train fault',
  SIGNAL_DELAY: 'Signal delay',
  OBSTRUCTION: 'Obstruction',
  CANNOT_CONTINUE: 'Cannot continue',
}

export const reasonCodes = ['TRAIN_FAULT', 'SIGNAL_DELAY', 'OBSTRUCTION', 'CANNOT_CONTINUE'] as const

export const reasonName = (code: string): string => reasonNames[code] ?? code
