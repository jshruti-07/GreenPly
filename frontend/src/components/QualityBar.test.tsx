import { render, screen } from '@testing-library/react';
import { QualityBar } from './QualityBar';

describe('QualityBar', () => {
  it('exposes value via progressbar role', () => {
    render(<QualityBar value={72} />);
    const bar = screen.getByRole('progressbar', { name: 'Completeness' });
    expect(bar).toHaveAttribute('aria-valuenow', '72');
    expect(screen.getByText('72%')).toBeInTheDocument();
  });

  it('clamps out-of-range values', () => {
    render(<QualityBar value={140} />);
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '100');
  });

  it('clamps negative values to zero', () => {
    render(<QualityBar value={-5} />);
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '0');
  });

  it('hides the label when requested', () => {
    render(<QualityBar value={50} hideLabel />);
    expect(screen.queryByText('50%')).not.toBeInTheDocument();
  });
});
