import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { walletApi } from '../api/walletApi';
import { paymentApi } from '../api/paymentApi';
import { getErrorMessage } from '../api/client';
import usePolling from '../hooks/usePolling';
import { useAuth } from '../auth/AuthContext';
import { homeOf } from '../auth/ProtectedRoute';
import { money } from '../utils/format';

const ENTRY_TYPE_LABELS = {
  0: 'CHARGE (Thanh toán / Thu nhập)',
  1: 'REFUND (Hoàn tiền)',
  CHARGE: 'CHARGE (Thanh toán / Thu nhập)',
  REFUND: 'REFUND (Hoàn tiền)',
};

export default function WalletPage() {
  const { user, logout } = useAuth();
  const load = useCallback(signal => walletApi.getMyWallet(signal), []);
  const { data: wallet, loading, error, refresh: loadWallet } = usePolling(load, false);

  const [lookupPaymentId, setLookupPaymentId] = useState('');
  const [selectedPayment, setSelectedPayment] = useState(null);
  const [paymentLoading, setPaymentLoading] = useState(false);
  const [paymentError, setPaymentError] = useState('');

  const isRider = user?.role === 'Rider';
  const backLink = homeOf(user?.role);
  const backState = isRider ? { showHome: true } : undefined;

  const entries = wallet?.entries ?? [];

  const fetchPaymentDetail = async id => {
    const trimmed = (id || '').trim();
    if (!trimmed) return;
    setPaymentLoading(true);
    setPaymentError('');
    try {
      const detail = await paymentApi.getById(trimmed);
      setSelectedPayment(detail);
    } catch (err) {
      setSelectedPayment(null);
      setPaymentError(getErrorMessage(err));
    } finally {
      setPaymentLoading(false);
    }
  };

  const handlePaymentLookupSubmit = e => {
    e.preventDefault();
    fetchPaymentDetail(lookupPaymentId);
  };

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

      {loading && <p>Đang tải thông tin ví...</p>}

      {!loading && error && (
        <div role="alert">
          <p>{error}</p>
          <button type="button" onClick={() => loadWallet()}>Thử lại</button>
        </div>
      )}

      {!loading && !error && wallet && (
        <>
          <section>
            <fieldset>
              <legend>Số dư hiện tại</legend>
              <p><strong>{money(wallet.balance ?? 0)}</strong></p>
            </fieldset>
          </section>

          {isRider && (
            <section>
              <h2>Tra cứu giao dịch theo Payment ID (GET /payments/&#123;id&#125;)</h2>
              <form onSubmit={handlePaymentLookupSubmit}>
                <label htmlFor="wallet-payment-id">Mã giao dịch (Payment ID) </label>
                <input
                  id="wallet-payment-id"
                  type="text"
                  size="40"
                  placeholder="Nhập hoặc chọn từ bảng bút toán bên dưới"
                  value={lookupPaymentId}
                  onChange={e => setLookupPaymentId(e.target.value)}
                  required
                />{' '}
                <button type="submit" disabled={paymentLoading}>
                  {paymentLoading ? 'Đang tải...' : 'Xem giao dịch'}
                </button>
              </form>

              {paymentError && <p role="alert">{paymentError}</p>}

              {selectedPayment && (
                <fieldset>
                  <legend>Chi tiết giao dịch {selectedPayment.id}</legend>
                  <p>Mã giao dịch (Payment ID): <code>{selectedPayment.id}</code></p>
                  <p>
                    Mã chuyến đi (Trip ID):{' '}
                    <Link to={`/rider/trips/${selectedPayment.tripId}`}>
                      <code>{selectedPayment.tripId}</code>
                    </Link>
                  </p>
                  <p>Số tiền: <strong>{money(selectedPayment.amount)}</strong></p>
                  <p>Phương thức: <code>{selectedPayment.method}</code></p>
                  <p>Trạng thái: <strong>{selectedPayment.status}</strong></p>
                  <p>Tài xế nhận: {money(selectedPayment.driverAmount)} | Phí nền tảng: {money(selectedPayment.platformAmount)}</p>
                  <p>Số lần thử (AttemptCount): {selectedPayment.attemptCount}</p>
                  {selectedPayment.failureReason && (
                    <p>Lý do thất bại: <code>{selectedPayment.failureReason}</code></p>
                  )}
                </fieldset>
              )}
            </section>
          )}

          <section>
            <h2>Lịch sử bút toán ({entries.length})</h2>

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
                    {isRider && <th>Thao tác</th>}
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
                        <td>
                          <strong>
                            {isPositive ? `+${money(amount)}` : `-${money(Math.abs(amount))}`}
                          </strong>
                        </td>
                        <td><code>{entry.paymentId}</code></td>
                        {isRider && (
                          <td>
                            <button
                              type="button"
                              disabled={paymentLoading}
                              onClick={() => {
                                setLookupPaymentId(entry.paymentId);
                                fetchPaymentDetail(entry.paymentId);
                              }}
                            >
                              Chi tiết
                            </button>
                          </td>
                        )}
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
