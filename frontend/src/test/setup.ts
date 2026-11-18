import '@testing-library/jest-dom/vitest';

/**
 * jsdom implements no layout, so it has no `scrollIntoView` at all — calling it
 * throws rather than doing nothing. Every browser has it.
 *
 * A no-op here keeps the difference from surfacing as an unhandled exception in
 * tests that merely happen to move focus. Tests that care about the call spy on
 * this and assert against it.
 */
if (!('scrollIntoView' in HTMLElement.prototype)) {
  Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
    configurable: true,
    writable: true,
    value: () => {},
  });
}
