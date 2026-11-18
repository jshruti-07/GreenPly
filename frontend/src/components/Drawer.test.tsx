import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Drawer } from './Drawer';

describe('Drawer', () => {
  it('is hidden from accessibility tree when closed', () => {
    render(
      <Drawer open={false} title="Edit product" onClose={() => undefined}>
        body
      </Drawer>,
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows title, body and footer when open', () => {
    render(
      <Drawer open title="Edit product" onClose={() => undefined} footer={<button>Save</button>}>
        <p>Form body</p>
      </Drawer>,
    );
    const dialog = screen.getByRole('dialog', { name: 'Edit product' });
    expect(dialog).toHaveClass('on');
    expect(screen.getByText('Form body')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
  });

  it('closes via close button and Escape', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <Drawer open title="Edit" onClose={onClose}>
        body
      </Drawer>,
    );
    await user.click(screen.getByRole('button', { name: 'Close' }));
    expect(onClose).toHaveBeenCalledTimes(1);
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(2);
  });
});
