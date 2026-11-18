import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { ErrorBoundary } from './ErrorBoundary';

function Boom({ fail }: { fail: boolean }): React.ReactElement {
  if (fail) {
    throw new Error('undefined is not a function');
  }
  return <p>content</p>;
}

describe('ErrorBoundary', () => {
  let consoleError: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    // React logs the caught error; keep the test output readable.
    consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });
  afterEach(() => consoleError.mockRestore());

  it('renders children when nothing throws', () => {
    render(
      <ErrorBoundary>
        <Boom fail={false} />
      </ErrorBoundary>,
    );

    expect(screen.getByText('content')).toBeInTheDocument();
  });

  it('shows a recoverable message instead of a blank screen', () => {
    render(
      <ErrorBoundary area="Products">
        <Boom fail />
      </ErrorBoundary>,
    );

    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong on this screen');
    expect(screen.getByText('undefined is not a function')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('recovers when the underlying problem is gone', async () => {
    const user = userEvent.setup();

    function Harness() {
      const [fail, setFail] = useState(true);
      return (
        <>
          <button onClick={() => setFail(false)}>fix it</button>
          <ErrorBoundary>
            <Boom fail={fail} />
          </ErrorBoundary>
        </>
      );
    }

    render(<Harness />);
    expect(screen.getByRole('alert')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'fix it' }));
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(screen.getByText('content')).toBeInTheDocument();
  });
});
