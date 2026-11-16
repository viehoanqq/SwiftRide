import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { walletApi } from '../api/walletApi';
import { getErrorMessage } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { money } from '../utils/format';

const ENTRY_TYPE_LABELS = {
  0: 'CHARGE (Thanh toán / Thu nhập)',
  1: 'REFUND (Hoàn tiền)',
  CHARGE: 'CHARGE (Thanh toán / Thu nhập)',
  REFUND: 'REFUND (Hoàn tiền)',
};

export default function WalletPage() {
  const { user, logout } = useAuth();
  const [wallet, setWallet] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const isDriver = user?.role === 'Driver';
  const backLink = isDriver ? '/driver' : '/rider';
  const backState = isDriver ? undefined : { showHome: true };

  const loadWallet = useCallback(async (signal) => {
    setLoading(true);
    setError('');
    try {
      const data = await walletApi.getMyWallet(signal);
      if (!signal?.aborted) {
        setWallet(data);
      }
    } catch (err) {
      if (!signal?.aborted && err.name !== 'CanceledError') {
        setError(getErrorMessage(err));
      }
    } finally {
      if (!signal?.aborted) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    loadWallet(controller.signal);
    return () => controller.abort();
  }, [loadWallet]);

  const entries = wallet?.entries ?? [];

  return (
    <main>
      <h1>Ví của tôi — {user?.name} ({user?.role})</h1>

      <p>
        <Link to={backLink} state={backState}>← Quay lại trang chính</Link>{' | '}
        <button type="button" disabled={loading} onClick={() => loadWallet()}>
          Tải lại
        </button>{' '}
        <button type="button" onClick={logout}>Đăng xuất</button>
      </p>

      {/* Trạng thái đang tải */}
      {loading && <p>Đang tải thông tin ví...</p>}

      {/* Trạng thái lỗi */}
      {!loading && error && (
        <div role="alert">
          <p>{error}</p>
          <button type="button" onClick={() => loadWallet()}>Thử lại</button>
        </div>
      )}

      {/* Nội dung khi tải thành công */}
      {!loading && !error && wallet && (
        <>
          <section>
            <fieldset>
              <legend>Số dư hiện tại</legend>
              <p><strong>{money(wallet.balance ?? 0)}</strong></p>
            </fieldset>
          </section>

          <section>
            <h2>Lịch sử bút toán</h2>

            {/* Trạng thái rỗng */}
            {entries.length === 0 ? (
              <p>Chưa có bút toán nào trong ví.</p>
            ) : (
              <table border="1" cellPadding="8" cellSpacing="0">
                <thead>
                  <tr>
                    <th>Thời gian</th>
                    <th>Loại bút toán</th>
                    <th>Số tiền (+/-)</th>
                    <th>Mã giao dịch (Payment ID)</th>
                  </tr>
                </thead>
                <tbody>
                  {entries.map((entry, index) => {
                    const amount = Number(entry.amount ?? 0);
                    const isPositive = amount >= 0;
                    const typeLabel = ENTRY_TYPE_LABELS[entry.entryType] ?? String(entry.entryType);

                    return (
                      <tr key={`${entry.paymentId}-${entry.entryType}-${index}`}>
                        <td>{new Date(entry.createdAt).toLocaleString('vi-VN')}</td>
                        <td>{typeLabel}</td>
                        <td style={{ color: isPositive ? 'green' : 'red', fontWeight: 'bold' }}>
                          {isPositive ? `+${money(amount)}` : `-${money(Math.abs(amount))}`}
                        </td>
                        <td><code>{entry.paymentId}</code></td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
          </section>
        </>
      )}
    </main>
  );
}