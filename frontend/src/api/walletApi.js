import client from './client';

export const walletApi = {
  getMyWallet: (signal) =>
    client.get('/wallets/me', { signal }).then(res => res.data).catch(error => {
      if (error.response?.data?.code === 'ACCOUNT_NOT_FOUND') {
        error.response.data.message = 'Ví đang được khởi tạo. Vui lòng thử lại sau vài giây.';
      }
      throw error;
    }),
};
