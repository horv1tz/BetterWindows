import { describe, expect, it } from 'vitest';
import { ONBOARDING_STEPS, canGoto, gotoStep, initialOnboarding, isFirstStep, isLastStep, nextStep, prevStep, progressOf, stepOf } from './onboarding';

describe('onboarding step machine', () => {
  it('has the documented order', () => {
    expect([...ONBOARDING_STEPS]).toEqual(['welcome', 'taskbar', 'appearance', 'dock', 'desktop', 'shortcuts', 'finish']);
  });
  it('walks forward and back and clamps at both ends', () => {
    let s = initialOnboarding();
    expect(isFirstStep(s)).toBe(true);
    expect(prevStep(s)).toBe(s);
    for (let i = 0; i < 20; i++) s = nextStep(s);
    expect(isLastStep(s)).toBe(true);
    expect(stepOf(s)).toBe('finish');
    expect(nextStep(s)).toBe(s);
    s = prevStep(s);
    expect(stepOf(s)).toBe('shortcuts');
  });
  it('remembers the furthest step: dots may jump back and forth but never ahead', () => {
    let s = initialOnboarding();
    s = nextStep(nextStep(nextStep(s))); // dock
    expect(s.furthest).toBe(3);
    s = gotoStep(s, 1);
    expect(stepOf(s)).toBe('taskbar');
    expect(s.furthest).toBe(3);
    expect(canGoto(s, 3)).toBe(true);
    expect(canGoto(s, 4)).toBe(false);
    expect(gotoStep(s, 6)).toBe(s);
    expect(gotoStep(s, -1)).toBe(s);
    expect(gotoStep(s, 1.5)).toBe(s);
    expect(stepOf(gotoStep(s, 3))).toBe('dock');
  });
  it('progress goes from 0 to 1', () => {
    let s = initialOnboarding();
    expect(progressOf(s)).toBe(0);
    for (let i = 0; i < 6; i++) s = nextStep(s);
    expect(progressOf(s)).toBe(1);
  });
});
