import { render, screen } from '@testing-library/react';
import { Chip, StatusBadge } from './Chip';

describe('Chip', () => {
  it('renders content with neutral tone by default', () => {
    render(<Chip>Label</Chip>);
    const chip = screen.getByText('Label');
    expect(chip).toHaveClass('chip', 's-neut');
  });

  it('applies the requested tone', () => {
    render(<Chip tone="warn">Pending</Chip>);
    expect(screen.getByText('Pending')).toHaveClass('s-warn');
  });

  it('renders a decorative dot when requested', () => {
    const { container } = render(<Chip dot>With dot</Chip>);
    const dot = container.querySelector('.bd');
    expect(dot).not.toBeNull();
    expect(dot).toHaveAttribute('aria-hidden', 'true');
  });
});

describe('StatusBadge', () => {
  it.each([
    ['Active', 's-ok'],
    ['InReview', 's-warn'],
    ['Discontinued', 's-bad'],
  ])('maps %s to tone class %s', (status, cls) => {
    render(<StatusBadge status={status} />);
    expect(screen.getByText(status)).toHaveClass(cls);
  });

  it('falls back to neutral for unknown states', () => {
    render(<StatusBadge status="SomethingNew" />);
    expect(screen.getByText('SomethingNew')).toHaveClass('s-neut');
  });
});
