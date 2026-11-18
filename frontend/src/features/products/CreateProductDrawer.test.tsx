import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CreateProductDrawer } from './CreateProductDrawer';
import { installFakeApi, type FakeApi } from './testApi';

function renderDrawer(overrides: Partial<Parameters<typeof CreateProductDrawer>[0]> = {}) {
  const onClose = vi.fn();
  const onCreated = vi.fn();
  render(<CreateProductDrawer open onClose={onClose} onCreated={onCreated} {...overrides} />);
  return { onClose, onCreated };
}

/** Fills the required wizard fields with valid values. */
async function fillRequired(user: ReturnType<typeof userEvent.setup>) {
  await user.selectOptions(screen.getByLabelText(/Category/), '2');
  await user.type(screen.getByLabelText(/Product code/), 'FD-90-GS-1000');
  await user.type(screen.getByLabelText(/Product Name/), 'Fire Door 90 Min');
  await user.selectOptions(screen.getByLabelText(/Base Language/), 'de-DE');
}

describe('CreateProductDrawer', () => {
  let api: FakeApi;

  afterEach(() => api?.restore());

  it('renders the seven v4.2 wizard fields', async () => {
    api = installFakeApi();
    renderDrawer();

    expect(await screen.findByLabelText(/Brand/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Product Family/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Category/)).toBeInTheDocument();
    expect(screen.getByLabelText(/SAP Number/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Product Name/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Owner/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Base Language/)).toBeInTheDocument();
  });

  it('populates Brand and Category from the lookup endpoints', async () => {
    api = installFakeApi();
    renderDrawer();

    const brand = await screen.findByLabelText(/Brand/);
    await waitFor(() => expect(brand).toHaveTextContent('BuildCore'));
    expect(screen.getByLabelText(/Category/)).toHaveTextContent('Fire Doors');
  });

  it('posts the wizard fields and reports the created product', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    const { onCreated } = renderDrawer();
    await screen.findByLabelText(/Brand/);

    await fillRequired(user);
    await user.selectOptions(screen.getByLabelText(/Owner/), 'Anna Becker');
    await user.type(screen.getByLabelText(/SAP Number/), '10000012345');
    await user.click(screen.getByRole('button', { name: 'Create product' }));

    await waitFor(() => expect(api.createBodies).toHaveLength(1));
    expect(api.createBodies[0]).toMatchObject({
      code: 'FD-90-GS-1000',
      name: 'Fire Door 90 Min',
      primaryNodeId: 2,
      baseLocale: 'de-DE',
      ownerDisplayName: 'Anna Becker',
      sapMaterialNo: '10000012345',
      // Category node carries the attribute set that drives completeness.
      attributeSetId: 7,
    });

    await waitFor(() => expect(onCreated).toHaveBeenCalledTimes(1));
  });

  it('allows an unassigned owner', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    renderDrawer();
    await screen.findByLabelText(/Brand/);

    await fillRequired(user);
    await user.click(screen.getByRole('button', { name: 'Create product' }));

    await waitFor(() => expect(api.createBodies).toHaveLength(1));
    expect(api.createBodies[0]).toMatchObject({ ownerDisplayName: null });
  });

  it('surfaces an RFC 7807 validation failure without closing', async () => {
    const user = userEvent.setup();
    api = installFakeApi({
      createError: {
        status: 409,
        title: 'Product.Code.Duplicate',
        detail: 'A product with this code already exists.',
      },
    });
    const { onCreated } = renderDrawer();
    await screen.findByLabelText(/Brand/);

    await fillRequired(user);
    await user.click(screen.getByRole('button', { name: 'Create product' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'A product with this code already exists.',
    );
    expect(onCreated).not.toHaveBeenCalled();
  });

  it('closes via Cancel', async () => {
    const user = userEvent.setup();
    api = installFakeApi();
    const { onClose } = renderDrawer();
    await screen.findByLabelText(/Brand/);

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
