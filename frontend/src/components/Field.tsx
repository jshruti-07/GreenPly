import { InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes, useId } from 'react';

interface FieldWrapperProps {
  label: string;
  required?: boolean;
  hint?: string;
  /** Renders the control, given the generated id for label association. */
  children: (id: string) => ReactNode;
}

function FieldWrapper({ label, required = false, hint, children }: FieldWrapperProps) {
  const id = useId();
  return (
    <div className="field">
      <label htmlFor={id}>
        {label}
        {required && (
          <span className="req" aria-hidden="true">
            {' '}
            *
          </span>
        )}
      </label>
      {children(id)}
      {hint && <div className="hint">{hint}</div>}
    </div>
  );
}

export interface TextFieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  hint?: string;
}

export function TextField({ label, hint, required, ...rest }: TextFieldProps) {
  return (
    <FieldWrapper label={label} required={required} hint={hint}>
      {(id) => <input id={id} className="fld" required={required} {...rest} />}
    </FieldWrapper>
  );
}

export interface TextAreaFieldProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label: string;
  hint?: string;
}

export function TextAreaField({ label, hint, required, ...rest }: TextAreaFieldProps) {
  return (
    <FieldWrapper label={label} required={required} hint={hint}>
      {(id) => <textarea id={id} className="fld" required={required} {...rest} />}
    </FieldWrapper>
  );
}

export interface SelectFieldProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string;
  hint?: string;
  options: ReadonlyArray<{ value: string; label: string }>;
}

export function SelectField({ label, hint, required, options, ...rest }: SelectFieldProps) {
  return (
    <FieldWrapper label={label} required={required} hint={hint}>
      {(id) => (
        <select id={id} className="sel" required={required} {...rest}>
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      )}
    </FieldWrapper>
  );
}

export interface SearchInputProps extends InputHTMLAttributes<HTMLInputElement> {
  /** Accessible name for the search box. */
  label: string;
}

/** Toolbar search box (.inp) with magnifier icon, per prototype toolbar. */
export function SearchInput({ label, ...rest }: SearchInputProps) {
  return (
    <div className="inp">
      <svg className="ic sm" viewBox="0 0 24 24" aria-hidden="true">
        <circle cx="11" cy="11" r="7" />
        <path d="M21 21l-4.3-4.3" />
      </svg>
      <input type="search" aria-label={label} {...rest} />
    </div>
  );
}
