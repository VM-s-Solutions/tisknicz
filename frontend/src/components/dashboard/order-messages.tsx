'use client';

import { useState, useRef, useEffect } from 'react';
import { Icon } from '@/components/ui/icon';

interface Message {
  id: string;
  sender_id: string;
  content: string;
  created_at: string;
}

interface OrderMessagesProps {
  orderId: string;
  currentUserId: string;
  initialMessages: Message[];
}

export function OrderMessages({ orderId, currentUserId, initialMessages }: OrderMessagesProps) {
  const [messages, setMessages] = useState<Message[]>(initialMessages);
  const [newMessage, setNewMessage] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState('');
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  // Poll for new messages every 15 seconds
  useEffect(() => {
    const interval = setInterval(async () => {
      try {
        const res = await fetch(`/api/orders/${orderId}/messages`);
        if (res.ok) {
          const data: Message[] = await res.json();
          setMessages(data);
        }
      } catch {
        // Silently fail on polling
      }
    }, 15000);

    return () => clearInterval(interval);
  }, [orderId]);

  async function handleSend(e: React.FormEvent) {
    e.preventDefault();
    if (!newMessage.trim() || sending) return;

    setSending(true);
    setError('');

    try {
      const res = await fetch(`/api/orders/${orderId}/messages`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: newMessage.trim() }),
      });

      if (!res.ok) {
        const data = await res.json();
        setError(data.error ?? 'Chyba při odesílání zprávy');
        setSending(false);
        return;
      }

      const message: Message = await res.json();
      setMessages((prev) => [...prev, message]);
      setNewMessage('');
    } catch {
      setError('Nepodařilo se odeslat zprávu.');
    } finally {
      setSending(false);
    }
  }

  return (
    <section className="rounded-2xl border border-zinc-800 bg-surface-card p-6">
      <h2 className="font-semibold text-white flex items-center gap-2">
        <Icon name="messageCircle" size={18} className="text-brand-400" />
        Zprávy
      </h2>

      {/* Messages list */}
      <div className="mt-4 max-h-80 overflow-y-auto space-y-3">
        {messages.length === 0 ? (
          <p className="py-8 text-center text-sm text-zinc-600">
            Zatím žádné zprávy. Napište první zprávu.
          </p>
        ) : (
          messages.map((msg) => {
            const isMine = msg.sender_id === currentUserId;
            return (
              <div
                key={msg.id}
                className={`flex ${isMine ? 'justify-end' : 'justify-start'}`}
              >
                <div className={`max-w-[80%] rounded-2xl px-4 py-2.5 ${
                  isMine
                    ? 'bg-brand-400/15 text-zinc-100'
                    : 'bg-zinc-800 text-zinc-200'
                }`}>
                  <p className="text-sm whitespace-pre-wrap">{msg.content}</p>
                  <p className={`mt-1 text-xs ${isMine ? 'text-brand-400/60' : 'text-zinc-500'}`}>
                    {new Date(msg.created_at).toLocaleTimeString('cs-CZ', { hour: '2-digit', minute: '2-digit' })}
                  </p>
                </div>
              </div>
            );
          })
        )}
        <div ref={messagesEndRef} />
      </div>

      {/* Input */}
      <form onSubmit={handleSend} className="mt-4 flex items-end gap-2">
        <div className="flex-1">
          <textarea
            value={newMessage}
            onChange={(e) => setNewMessage(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                handleSend(e);
              }
            }}
            rows={2}
            placeholder="Napište zprávu..."
            className="block w-full rounded-xl border border-zinc-700 bg-zinc-900 px-4 py-2.5 text-sm text-zinc-100 transition-colors focus:border-brand-400 focus:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-brand-400/20 resize-none placeholder:text-zinc-600"
          />
        </div>
        <button
          type="submit"
          disabled={sending || !newMessage.trim()}
          className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-brand-400 bg-brand-400/10 text-brand-400 transition-all hover:bg-brand-400/20 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {sending ? (
            <svg className="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
          ) : (
            <Icon name="send" size={16} />
          )}
        </button>
      </form>

      {error && (
        <p className="mt-2 text-sm text-red-400 flex items-center gap-1">
          <Icon name="xCircle" size={14} />
          {error}
        </p>
      )}
    </section>
  );
}
