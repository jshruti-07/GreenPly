import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Button } from './Button';

describe('Button', () => {
  it('renders with default variant and type button', () => {
    render(<Button>Save</Button>);
    const btn = screen.getByRole('button', { name: 'Save' });
    expect(btn).toHaveClass('btn');
    expect(btn).not.toHaveClass('pri');
    expect(btn).toHaveAttribute('type', 'button');
  });

  it.each([
    ['primary', 'pri'],
    ['green', 'grn'],
    ['ghost', 'ghost'],
    ['danger', 'danger'],
  ] as const)('applies %s variant class', (variant, cls) => {
    render(<Button variant={variant}>X</Button>);
    expect(screen.getByRole('button')).toHaveClass(cls);
  });

  it('applies small size and icon-only classes', () => {
    render(
      <Button size="sm" iconOnly aria-label="Close">
        ×
      </Button>,
    );
    const btn = screen.getByRole('button', { name: 'Close' });
    expect(btn).toHaveClass('sm');
    expect(btn).toHaveClass('ico');
  });

  it('fires onClick and respects disabled', async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    const { rerender } = render(<Button onClick={onClick}>Go</Button>);
    await user.click(screen.getByRole('button', { name: 'Go' }));
    expect(onClick).toHaveBeenCalledTimes(1);

    rerender(
      <Button onClick={onClick} disabled>
        Go
      </Button>,
    );
    await user.click(screen.getByRole('button', { name: 'Go' }));
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});
