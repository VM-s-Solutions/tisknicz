import { redirect } from 'next/navigation';

export const metadata = {
  title: 'Registrace výrobce — Makables',
};

export default function RegisterMakerPage() {
  redirect('/register?type=maker');
}
