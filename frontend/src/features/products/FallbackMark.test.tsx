import { render, screen } from '@testing-library/react';
import { FallbackMark } from './FallbackMark';

/**
 * The marker is the visible half of the fallback decision (2026-08-17): fall
 * back so the grid stays readable, but never silently, because a PIM that hides
 * translation gaps is working against its own purpose.
 */
describe('FallbackMark', () => {
  it('renders nothing when the text is in the requested language', () => {
    const { container } = render(<FallbackMark actual="en-GB" requested="en-GB" />);
    expect(container).toBeEmptyDOMElement();
  });

  it('names the language actually shown when the row fell back', () => {
    render(<FallbackMark actual="de-DE" requested="en-GB" />);

    expect(screen.getByText('de-DE')).toBeInTheDocument();
    expect(screen.getByTitle('No en-GB translation — showing de-DE')).toBeInTheDocument();
  });

  it('distinguishes "no translation at all" from a fallback', () => {
    render(<FallbackMark actual={null} requested="en-GB" />);

    // A different and worse state: there is no name in any language and the
    // product code is standing in. Marking it the same as a fallback would
    // understate it.
    expect(screen.getByText('untranslated')).toBeInTheDocument();
  });

  it('stays silent when the API did not send the field', () => {
    // An older API build. Undefined means "unknown", which is not the same as
    // "fallback" — guessing here would put a warning on every row.
    const { container } = render(<FallbackMark actual={undefined} requested="en-GB" />);
    expect(container).toBeEmptyDOMElement();
  });
});
