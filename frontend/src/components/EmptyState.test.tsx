import { render, screen } from '@testing-library/react';
import { EmptyState } from './EmptyState';

describe('EmptyState', () => {
  it('renders title only', () => {
    render(<EmptyState title="No products yet" />);
    expect(screen.getByText('No products yet')).toBeInTheDocument();
  });

  it('renders description and action', () => {
    render(
      <EmptyState
        title="No products yet"
        description="Create your first product to get started."
        action={<button>Create product</button>}
      />,
    );
    expect(screen.getByText('Create your first product to get started.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create product' })).toBeInTheDocument();
  });
});
