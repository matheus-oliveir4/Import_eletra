import type { NextConfig } from "next";

const legacyApiOrigin = process.env.LEGACY_API_ORIGIN ?? "http://localhost:5000";

const nextConfig: NextConfig = {
  async rewrites() {
    if (process.env.NODE_ENV !== "development") return [];

    return [
      { source: "/auth/:path*", destination: `${legacyApiOrigin}/auth/:path*` },
      { source: "/api/:path*", destination: `${legacyApiOrigin}/api/:path*` },
    ];
  },
};

export default nextConfig;
