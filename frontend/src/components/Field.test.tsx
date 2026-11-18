import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SearchInput, SelectField, TextAreaField, TextField } from './Field';

describe('TextField', () => {
  it('associates label with input', () => {
    render(<TextField label="Product code" />);
    expect(screen.getByLabelText('Product code')).toBeInstanceOf(HTMLInputElement);
  });

  it('shows hint and required marker', () => {
    render(<TextField label="Code" required hint="Unique SKU code" />);
    expect(screen.getByText('Unique SKU code')).toBeInTheDocument();
    expect(screen.getByLabelText(/Code/)).toBeRequired();
  });
});

describe('TextAreaField', () => {
  it('renders a textarea bound to its label', () => {
    render(<TextAreaField label="Description" />);
    expect(screen.getByLabelText('Description')).toBeInstanceOf(HTMLTextAreaElement);
  });
});

describe('SelectField', () => {
  it('renders options and supports selection', async () => {
    const user = userEvent.setup();
    render(
      <SelectField
        label="Status"
        defaultValue="draft"
        options={[
          { value: 'draft', label: 'Draft' },
          { value: 'active', label: 'Active' },
        ]}
      />,
    );
    const select = screen.getByLabelText('Status');
    await user.selectOptions(select, 'active');
    expect(select).toHaveValue('active');
  });
});

describe('SearchInput', () => {
  it('is a labelled search box that accepts typing', async () => {
    const user = userEvent.setup();
    render(<SearchInput label="Search products" />);
    const box = screen.getByRole('searchbox', { name: 'Search products' });
    await user.type(box, 'bolt');
    expect(box).toHaveValue('bolt');
  });
});
