import { Component, type ErrorInfo, type ReactNode } from 'react';

export interface ErrorBoundaryProps {
  children: ReactNode;
  /** Identifies the boundary in logs, e.g. the route name. */
  area?: string;
}

interface ErrorBoundaryState {
  error: Error | null;
}

/**
 * Stops one bad render from blanking the whole application.
 *
 * Added after a real incident (2026-08-14): the UI was hot-reloaded against an
 * older API build that did not yet return `channelReadiness`, so a single
 * `undefined.map()` unmounted the entire React tree and the user saw a white
 * screen with no explanation.
 *
 * Class component on purpose — React has no hook equivalent of
 * componentDidCatch.
 */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    // Console until a front-end telemetry sink exists (observability backlog).
    console.error(`[${this.props.area ?? 'app'}] render failed`, error, info.componentStack);
  }

  private readonly handleReset = () => this.setState({ error: null });

  override render(): ReactNode {
    const { error } = this.state;
    if (!error) {
      return this.props.children;
    }

    return (
      <div className="pad">
        <div className="card pad" role="alert">
          <h2 style={{ fontSize: 15, margin: '0 0 6px' }}>Something went wrong on this screen</h2>
          <p className="muted" style={{ margin: '0 0 12px' }}>
            The rest of the application is still usable. If this keeps happening, the API and the
            UI may be running mismatched builds — restart both.
          </p>
          <pre
            style={{
              background: 'var(--c1)',
              border: '1px solid var(--line)',
              borderRadius: 'var(--r2)',
              padding: 12,
              fontSize: 11.5,
              overflowX: 'auto',
              margin: '0 0 12px',
            }}
          >
            {error.message}
          </pre>
          <button type="button" className="btn" onClick={this.handleReset}>
            Try again
          </button>
        </div>
      </div>
    );
  }
}
