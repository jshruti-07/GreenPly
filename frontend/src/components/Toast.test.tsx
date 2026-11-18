import { act, fireEvent, render, screen } from '@testing-library/react';
import { ToastProvider, useToast } from './Toast';

function Demo() {
  const { showToast } = useToast();
  return (
    <div>
      <button onClick={() => showToast('Product saved')}>notify</button>
      <button onClick={() => showToast('Save failed', 'error')}>fail</button>
    </div>
  );
}

describe('Toast', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  // fireEvent (not userEvent) on purpose: userEvent's internal delays deadlock
  // under fake timers, which this suite needs for auto-dismiss assertions.
  it('shows a toast and auto-dismisses it', () => {
    render(
      <ToastProvider>
        <Demo />
      </ToastProvider>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'notify' }));
    expect(screen.getByText('Product saved')).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(4100);
    });
    expect(screen.queryByText('Product saved')).not.toBeInTheDocument();
  });

  it('renders error kind with err class', () => {
    render(
      <ToastProvider>
        <Demo />
      </ToastProvider>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'fail' }));
    expect(screen.getByText('Save failed')).toHaveClass('err');
  });

  it('throws when used outside provider', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    expect(() => render(<Demo />)).toThrow(/inside <ToastProvider>/);
    spy.mockRestore();
  });
});
