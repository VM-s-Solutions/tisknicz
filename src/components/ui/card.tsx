import { type HTMLAttributes } from 'react';

interface CardProps extends HTMLAttributes<HTMLDivElement> {
  padding?: 'none' | 'sm' | 'md' | 'lg';
  hover?: boolean;
}

const paddingStyles = {
  none: '',
  sm: 'p-4',
  md: 'p-6',
  lg: 'p-8',
};

export function Card({ padding = 'md', hover = false, className = '', children, ...props }: CardProps) {
  return (
    <div
      className={`rounded-2xl border border-zinc-100 bg-white shadow-md ${hover ? 'transition-all duration-300 hover:-translate-y-1 hover:shadow-xl' : ''} ${paddingStyles[padding]} ${className}`}
      {...props}
    >
      {children}
    </div>
  );
}
