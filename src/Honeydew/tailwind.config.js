module.exports = {
  purge: [
    './**/*.cshtml',
    './**/*.cs',
    './**/*.html',
  ],
  theme: {
    extend: {},
  },
  variants: {},
  plugins: [
    require('@tailwindcss/custom-forms'),
  ]
}
