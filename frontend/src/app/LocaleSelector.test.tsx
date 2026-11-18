import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { installFakeApi } from '../features/products/testApi';
import { LocaleProvider, STORAGE_KEY } from './LocaleContext';
import { LocaleSelector } from './LocaleSelector';

function renderSelector() {
  return render(
    <LocaleProvider>
      <LocaleSelector />
    </LocaleProvider>,
  );
}

describe('LocaleSelector', () => {
  beforeEach(() => {
    installFakeApi();
    window.localStorage.clear();
  });

  it('offers every configured language once the list loads', async () => {
    renderSelector();

    const select = await screen.findByLabelText('Data language');
    expect(select).toHaveValue('en-GB');
    expect(screen.getByRole('option', { name: /Deutsch/ })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /English \(UK\)/ })).toBeInTheDocument();
  });

  it('marks the base locale as the source language', async () => {
    renderSelector();

    // The distinction matters to a translator: one of these is the language
    // everything is translated FROM, and it is not otherwise visible.
    expect(await screen.findByRole('option', { name: 'Deutsch (source)' })).toBeInTheDocument();
  });

  it('defaults to English even though the base locale is German', async () => {
    renderSelector();

    expect(await screen.findByLabelText('Data language')).toHaveValue('en-GB');
  });

  it('remembers the choice across mounts', async () => {
    const user = userEvent.setup();
    const { unmount } = renderSelector();

    await user.selectOptions(await screen.findByLabelText('Data language'), 'de-DE');
    await waitFor(() => expect(window.localStorage.getItem(STORAGE_KEY)).toBe('de-DE'));

    unmount();
    renderSelector();

    expect(await screen.findByLabelText('Data language')).toHaveValue('de-DE');
  });

  it('falls back when the stored language no longer exists', async () => {
    // A locale removed since the preference was saved would otherwise send
    // locale=xx-XX on every request and 400 on every screen.
    window.localStorage.setItem(STORAGE_KEY, 'xx-XX');

    renderSelector();

    expect(await screen.findByLabelText('Data language')).toHaveValue('en-GB');
  });

  it('hides itself when the catalogue has only one language', async () => {
    installFakeApi();
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname === '/api/v1/locales') {
        return new Response(JSON.stringify([{ code: 'en-GB', name: 'English (UK)', isBase: true }]), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
    }) as typeof fetch;

    renderSelector();

    // A selector with one option is furniture; it implies a choice that is not
    // there.
    await waitFor(() => expect(screen.queryByLabelText('Data language')).not.toBeInTheDocument());
  });
});
