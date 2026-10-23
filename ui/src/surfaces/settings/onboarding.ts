/** First-run wizard step machine (pure, unit-tested). */
export const ONBOARDING_STEPS = ['welcome', 'taskbar', 'appearance', 'dock', 'desktop', 'shortcuts', 'finish'] as const;
export type OnboardingStep = (typeof ONBOARDING_STEPS)[number];

export interface OnboardingState {
  index: number;
  /** highest step reached; the progress dots can jump anywhere up to it */
  furthest: number;
}

export const initialOnboarding = (): OnboardingState => ({ index: 0, furthest: 0 });
export const stepOf = (s: OnboardingState): OnboardingStep => ONBOARDING_STEPS[s.index];
export const isFirstStep = (s: OnboardingState): boolean => s.index === 0;
export const isLastStep = (s: OnboardingState): boolean => s.index === ONBOARDING_STEPS.length - 1;

export function nextStep(s: OnboardingState): OnboardingState {
  if (isLastStep(s)) return s;
  const index = s.index + 1;
  return { index, furthest: Math.max(s.furthest, index) };
}
export function prevStep(s: OnboardingState): OnboardingState {
  return s.index === 0 ? s : { ...s, index: s.index - 1 };
}
export const canGoto = (s: OnboardingState, index: number): boolean => Number.isInteger(index) && index >= 0 && index <= s.furthest && index < ONBOARDING_STEPS.length;
export function gotoStep(s: OnboardingState, index: number): OnboardingState {
  return canGoto(s, index) ? { ...s, index } : s;
}
/** 0..1 for the progress bar (the first step is 0, the last is 1). */
export const progressOf = (s: OnboardingState): number => s.index / (ONBOARDING_STEPS.length - 1);
