import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ProductCell, Table, Td, Th } from './Table';

describe('Table', () => {
  function renderTable(onSort?: () => void, sorted?: 'asc' | 'desc') {
    return render(
      <Table>
        <thead>
          <tr>
            <Th onSort={onSort} sorted={sorted}>
              Name
            </Th>
            <Th>Status</Th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <Td>Anchor bolt</Td>
            <Td>Active</Td>
          </tr>
        </tbody>
      </Table>,
    );
  }

  it('renders a table with rows and cells', () => {
    renderTable();
    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Anchor bolt' })).toBeInTheDocument();
  });

  it('compact mode adds the compact class', () => {
    render(
      <Table compact>
        <tbody>
          <tr>
            <Td>x</Td>
          </tr>
        </tbody>
      </Table>,
    );
    expect(screen.getByRole('table')).toHaveClass('compact');
  });

  it('sortable header fires onSort and exposes aria-sort', async () => {
    const user = userEvent.setup();
    const onSort = vi.fn();
    renderTable(onSort, 'asc');
    const th = screen.getByRole('columnheader', { name: /Name/ });
    expect(th).toHaveAttribute('aria-sort', 'ascending');
    await user.click(th);
    expect(onSort).toHaveBeenCalledTimes(1);
  });
});

describe('ProductCell', () => {
  it('shows name and sku', () => {
    render(<ProductCell name="Anchor bolt M12" sku="FD-90-GS-1000" />);
    expect(screen.getByText('Anchor bolt M12')).toBeInTheDocument();
    expect(screen.getByText('FD-90-GS-1000')).toBeInTheDocument();
  });
});
