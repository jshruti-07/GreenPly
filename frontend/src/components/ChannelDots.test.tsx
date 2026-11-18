import { render, screen } from '@testing-library/react';
import { ChannelDots } from './ChannelDots';

const CHANNELS = ['Website', 'Dealer Portal', 'Mobile App', 'API Feed'] as const;

describe('ChannelDots', () => {
  it('renders one dot per channel with the right tone', () => {
    const { container } = render(<ChannelDots levels={[2, 1, 0, 2]} channels={CHANNELS} />);

    const dots = container.querySelectorAll('.chdots i');
    expect(dots).toHaveLength(4);
    expect(dots[0]).toHaveClass('ok');
    expect(dots[1]).toHaveClass('warn');
    expect(dots[2]).toHaveClass('no');
  });

  it('describes readiness per channel and marks it derived', () => {
    render(<ChannelDots levels={[2, 1, 0, 0]} channels={CHANNELS} />);

    const label = screen.getByRole('img').getAttribute('aria-label');
    expect(label).toContain('(derived)');
    expect(label).toContain('Website: ready');
    expect(label).toContain('Dealer Portal: partially ready');
    expect(label).toContain('Mobile App: not ready');
  });

  // Regression: an older API build omitted this field and `.map()` on
  // undefined blanked the entire application (incident 2026-08-14).
  it.each([
    ['undefined', undefined],
    ['null', null],
    ['empty', [] as number[]],
  ])('degrades to neutral dots when levels are %s', (_label, levels) => {
    const { container } = render(<ChannelDots levels={levels} channels={CHANNELS} />);

    const dots = container.querySelectorAll('.chdots i');
    expect(dots).toHaveLength(4);
    dots.forEach((dot) => expect(dot).toHaveClass('no'));
    expect(screen.getByRole('img').getAttribute('aria-label')).toContain(
      'not yet reported by the API',
    );
  });

  it('ignores extra levels and fills missing ones', () => {
    const { container } = render(<ChannelDots levels={[2, 2, 2, 2, 2, 2]} channels={CHANNELS} />);
    expect(container.querySelectorAll('.chdots i')).toHaveLength(4);

    const { container: short } = render(<ChannelDots levels={[2]} channels={CHANNELS} />);
    const dots = short.querySelectorAll('.chdots i');
    expect(dots[0]).toHaveClass('ok');
    expect(dots[3]).toHaveClass('no');
  });

  it('treats an out-of-range level as not ready', () => {
    const { container } = render(<ChannelDots levels={[99, -1, 1.5, 3]} channels={CHANNELS} />);
    container.querySelectorAll('.chdots i').forEach((dot) => expect(dot).toHaveClass('no'));
  });
});
