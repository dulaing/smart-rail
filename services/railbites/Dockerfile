# Build from the repository root: docker build -f services/railbites/Dockerfile .
FROM node:24-alpine AS build
WORKDIR /app
COPY services/railbites/package*.json ./
RUN npm ci
COPY services/railbites/tsconfig.json ./
COPY services/railbites/src ./src
RUN npm run build && npm prune --omit=dev

FROM node:24-alpine
WORKDIR /app
ENV NODE_ENV=production PORT=8080
COPY --from=build /app/package.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
EXPOSE 8080
USER node
CMD ["node", "dist/index.js"]
