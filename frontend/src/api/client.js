import axios from 'axios';

const TOKEN_KEY = 'accessToken';

export const tokenStorage = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: token => localStorage.setItem(TOKEN_KEY, token),
  clear: () => localStorage.removeItem(TOKEN_KEY),
};

// Mọi request đều đi qua API Gateway
const client = axios.create({ baseURL: 'http://localhost:5000' });

// Trước khi gửi: tự gắn header Authorization nếu đã có token
client.interceptors.request.use(config => {
  const token = tokenStorage.get();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// Sau khi nhận: token hết hạn / không hợp lệ (401) thì xóa token và về trang đăng nhập.
// Bỏ qua chính API login, vì login sai mật khẩu cũng trả 401 và cần hiện lỗi tại chỗ.
client.interceptors.response.use(
  response => response,
  error => {
    const isLogin = error.config?.url?.includes('/auth/login');
    if (error.response?.status === 401 && !isLogin) {
      tokenStorage.clear();
      window.location.href = '/login';
    }
    return Promise.reject(error);
  },
);

// Lấy message lỗi từ backend ({ code, message }) hoặc lỗi kiểm tra dữ liệu của ASP.NET ({ errors })
export function getErrorMessage(error) {
  const data = error.response?.data;
  if (data?.message) return data.message;
  if (data?.errors) return Object.values(data.errors).flat().join(' ');
  if (!error.response) return 'Không kết nối được máy chủ';
  return 'Có lỗi xảy ra, vui lòng thử lại';
}

export default client;
