import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ToastProvider } from '../../components/Toast';
import { LocalizationTab } from './LocalizationTab';
import type { ProductLocale, ProductLocalization, TranslationField } from './types';

const LEAD = 'AB-CRAFT-EXTENDED-COVER-PLATE-31-X-30-CM';

function field(overrides: Partial<TranslationField> = {}): TranslationField {
  return {
    field: 'name',
    label: 'Product name',
    baseValue: 'Craft Extended Abdeckplatte Tropfkante 31 x 30 cm',
    value: 'Craft Extended cover plate 31 x 30 cm',
    isRequired: true,
    isMultiline: false,
    isAttribute: false,
    ...overrides,
  };
}

function locale(overrides: Partial<ProductLocale> = {}): ProductLocale {
  return {
    localeCode: 'en-GB',
    localeName: 'English (UK)',
    isBase: false,
    translationStatus: 'pending',
    source: 'human',
    updatedAtUtc: '2026-08-16T09:00:00Z',
    fieldCount: 2,
    filledCount: 1,
    completeness: 50,
    fields: [
      field(),
      field({
        field: 'shortDesc',
        label: 'Short description',
        baseValue: 'Feine Struktur in warmem Beige.',
        value: null,
        isRequired: false,
        isMultiline: true,
      }),
    ],
    ...overrides,
  };
}

interface Api {
  puts: Array<{ url: string; body: Record<string, unknown> }>;
}

function installApi(
  options: {
    payload?: Partial<ProductLocalization>;
    writeError?: { status: number; title: string; detail: string };
    loadError?: { status: number; title: string; detail: string };
  } = {},
): Api {
  const puts: Api['puts'] = [];

  const body = (): ProductLocalization => ({
    productCode: LEAD,
    baseLocale: 'de-DE',
    rowVersion: 4,
    pendingLocaleCount: 1,
    locales: [
      locale({
        localeCode: 'de-DE',
        localeName: 'Deutsch',
        isBase: true,
        translationStatus: 'approved',
        completeness: 100,
        filledCount: 2,
      }),
      locale(),
    ],
    ...options.payload,
  });

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      const problem = (p: { status: number; title: string; detail: string }) =>
        new Response(JSON.stringify(p), {
          status: p.status,
          headers: { 'Content-Type': 'application/problem+json' },
        });

      const ok = () =>
        new Response(JSON.stringify(body()), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });

      if (method === 'GET') {
        return options.loadError ? problem(options.loadError) : ok();
      }

      if (options.writeError) {
        return problem(options.writeError);
      }

      puts.push({ url, body: JSON.parse(String(init?.body)) as Record<string, unknown> });
      return ok();
    }),
  );

  return { puts };
}

function renderTab(onChanged?: (rowVersion: number) => void) {
  return render(
    <ToastProvider>
      <LocalizationTab code={LEAD} onChanged={onChanged} />
    </ToastProvider>,
  );
}

describe('LocalizationTab', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the master text beside the target, and only the target is editable', async () => {
    // German copy is owned by General and Attributes. An editable master column
    // would invite a translator to "fix" the source in a screen with none of
    // that field's validation.
    installApi();
    renderTab();

    const base = await screen.findByLabelText(/Product name · Deutsch/);
    const target = screen.getByLabelText(/Product name · English \(UK\)/);

    expect(base).toHaveValue('Craft Extended Abdeckplatte Tropfkante 31 x 30 cm');
    expect(base).toHaveAttribute('readonly');
    expect(target).not.toHaveAttribute('readonly');
  });

  it('opens on the language that still needs work, not on the first one', async () => {
    installApi();
    renderTab();

    expect(
      await screen.findByRole('region', { name: 'English (UK) translation' }),
    ).toBeInTheDocument();
  });

  it('does not offer the base locale as a translation target', async () => {
    installApi();
    renderTab();

    const languages = await screen.findByRole('region', { name: 'Languages' });
    expect(within(languages).getByRole('button', { name: /Deutsch/ })).toBeDisabled();
    expect(within(languages).getByRole('button', { name: /English/ })).toBeEnabled();
  });

  it('reports how much of the language is done', async () => {
    installApi();
    renderTab();

    expect(await screen.findByText('1 of 2 fields translated')).toBeInTheDocument();
  });

  it('sends only the fields that were edited', async () => {
    // A translator changing one sentence should not cause every other field to
    // be rewritten with its current value — that would stamp the whole record
    // as freshly translated.
    const api = installApi();
    renderTab();

    const target = await screen.findByLabelText(/Short description · English \(UK\)/);
    await userEvent.type(target, 'Fine structure in warm beige.');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].url).toContain('/localization/en-GB');
    expect(api.puts[0].body).toMatchObject({
      values: [{ field: 'shortDesc', value: 'Fine structure in warm beige.' }],
      translationStatus: null,
      rowVersion: 4,
    });
  });

  it('clears a translation by sending null, not an empty string', async () => {
    const api = installApi();
    renderTab();

    const target = await screen.findByLabelText(/Product name · English \(UK\)/);
    await userEvent.clear(target);
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].body).toMatchObject({ values: [{ field: 'name', value: null }] });
  });

  it('keeps Save disabled until something changes', async () => {
    installApi();
    renderTab();

    expect(await screen.findByRole('button', { name: 'Save' })).toBeDisabled();

    await userEvent.type(
      screen.getByLabelText(/Short description · English \(UK\)/),
      'x',
    );
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
  });

  it('approves the language as an explicit act, carrying any pending edits', async () => {
    const api = installApi();
    renderTab();

    await userEvent.type(
      await screen.findByLabelText(/Short description · English \(UK\)/),
      'Done.',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Approve language' }));

    await waitFor(() => expect(api.puts).toHaveLength(1));
    expect(api.puts[0].body).toMatchObject({
      translationStatus: 'approved',
      values: [{ field: 'shortDesc', value: 'Done.' }],
    });
  });

  it('marks a localizable attribute as one, so its different storage is visible', async () => {
    installApi({
      payload: {
        locales: [
          locale({ localeCode: 'de-DE', localeName: 'Deutsch', isBase: true }),
          locale({
            fields: [
              field({
                field: 'product_standard',
                label: 'Produktnorm',
                isRequired: false,
                isAttribute: true,
              }),
            ],
          }),
        ],
      },
    });
    renderTab();

    expect(await screen.findByLabelText(/Produktnorm · English \(UK\) · attribute/)).toBeInTheDocument();
  });

  it('surfaces a rejected save and keeps the edit on screen', async () => {
    installApi({
      writeError: {
        status: 400,
        title: 'Bad Request',
        detail: "'Product name' is required and cannot be cleared.",
      },
    });
    renderTab();

    const target = await screen.findByLabelText(/Product name · English \(UK\)/);
    await userEvent.clear(target);
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(
      await screen.findByText("'Product name' is required and cannot be cleared."),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Product name · English \(UK\)/)).toHaveValue('');
  });

  it('reports the new rowVersion so the rest of the page does not go stale', async () => {
    const onChanged = vi.fn();
    installApi();
    renderTab(onChanged);

    await userEvent.type(
      await screen.findByLabelText(/Short description · English \(UK\)/),
      'x',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(onChanged).toHaveBeenCalledWith(4));
  });

  it('says so when only the base locale is configured', async () => {
    installApi({
      payload: {
        pendingLocaleCount: 0,
        locales: [locale({ localeCode: 'de-DE', localeName: 'Deutsch', isBase: true })],
      },
    });
    renderTab();

    expect(await screen.findByText(/nothing to translate yet/)).toBeInTheDocument();
  });

  it('offers no machine-translation action, because no service has been chosen', async () => {
    installApi();
    renderTab();

    await screen.findByRole('region', { name: 'Languages' });
    expect(screen.queryByRole('button', { name: /translate/i })).not.toBeInTheDocument();
  });

  it('offers a retry when the tab cannot load', async () => {
    installApi({
      loadError: { status: 500, title: 'Error', detail: 'Could not reach the catalog service.' },
    });
    renderTab();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Could not reach the catalog service.',
    );
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
