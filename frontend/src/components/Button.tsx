import { ButtonHTMLAttributes, ReactNode } from 'react';

export type ButtonVariant = 'default' | 'primary' | 'green' | 'ghost' | 'danger';
export type ButtonSize = 'md' | 'sm';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Icon-only square button; provide aria-label. */
  iconOnly?: boolean;
  children?: ReactNode;
}

const variantClass: Record<ButtonVariant, string> = {
  default: '',
  primary: 'pri',
  green: 'grn',
  ghost: 'ghost',
  danger: 'danger',
};

export function Button({
  variant = 'default',
  size = 'md',
  iconOnly = false,
  className,
  type = 'button',
  children,
  ...rest
}: ButtonProps) {
  const classes = ['btn', variantClass[variant], size === 'sm' ? 'sm' : '', iconOnly ? 'ico' : '', className]
    .filter(Boolean)
    .join(' ');
  return (
    <button type={type} className={classes} {...rest}>
      {children}
    </button>
  );
}
